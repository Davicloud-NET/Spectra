using System.Globalization;
using System.Text;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// The Luau of a script that makes random writes: to its own node, to the
// node above it and to the root, a few each tick. First one to every member
// of each of the three, then the random ones. Every write is in a pcall,
// and the script ends by printing how many were refused and how many each
// member took. It is one line: every script ends on the same tick, and
// scripts may print only so many lines in one.
internal static class WritingScript
{
    private const int WritesPerTick = 4;

    private const string EndWord = "refused";

    // Indexes into the script's table of targets.
    private const int TargetCount = 3;

    private static int SweepWrites => TargetCount * ScriptWrites.Settable.Count;

    public static string From(RandomWrites random, int writes)
    {
        // The wait comes before the first write of any script, so what each
        // script read at its start is what the level was authored with.
        var text = new StringBuilder()
            .AppendLine("local targets = { script, script.Parent, workspace }")
            .AppendLine("local atStart = {}")
            .AppendLine("for i, target in targets do")
            .AppendLine($"    atStart[i] = {{ {MembersOf("target")} }}")
            .AppendLine("end")
            .AppendLine("local taken, refused = {}, 0")
            .AppendLine("local function try(member, write)")
            .AppendLine("    if pcall(write) then taken[member] = (taken[member] or 0) + 1 else refused += 1 end")
            .AppendLine("end")
            .AppendLine("task.wait()");

        int written = 0;
        for (int target = 1; target <= TargetCount; target++)
        {
            foreach (SettableMember member in ScriptWrites.Settable)
                Append(text, random.Taken(member), target, ref written);
        }

        // The random writes start on a tick of their own. One that puts a
        // value back would else hide a write of the same tick from a look.
        text.AppendLine("task.wait()");
        for (int i = 0; i < writes; i++)
            Append(text, random.Next(), 1 + random.Pick(TargetCount), ref written);

        return text
            .AppendLine("local counts = {}")
            .AppendLine("for member, count in taken do table.insert(counts, `{member}={count}`) end")
            .AppendLine($"print('{EndWord}', refused, table.concat(counts, ' '))")
            .ToString();
    }

    // The ticks a script of so many random writes needs to reach its last line.
    public static int TicksFor(int writes) => 4 + ((SweepWrites + writes) / WritesPerTick);

    // What the scripts that have ended counted, summed: how many scripts,
    // the writes each member took, and the writes that were refused.
    public static (int Scripts, IReadOnlyDictionary<string, int> Taken, int Refused) Tally(IEnumerable<string> printed)
    {
        int scripts = 0;
        int refused = 0;
        var taken = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string line in printed)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != EndWord)
                continue;

            scripts++;
            refused += int.Parse(parts[1], CultureInfo.InvariantCulture);
            foreach (string count in parts.AsSpan(2))
            {
                int split = count.IndexOf('=');
                string member = count[..split];
                taken[member] = taken.GetValueOrDefault(member) + int.Parse(count[(split + 1)..], CultureInfo.InvariantCulture);
            }
        }

        return (scripts, taken, refused);
    }

    private static void Append(StringBuilder text, RandomWrite write, int target, ref int written)
    {
        string member = write.Member.Name;
        string value = write.PutsBack ? $"atStart[{target}].{member}" : write.Luau;
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"try('{member}', function() targets[{target}].{member} = {value} end)");

        if (++written % WritesPerTick == 0)
            text.AppendLine("task.wait()");
    }

    // Every member a script can set, read from one node: "Name = node.Name, ...".
    private static string MembersOf(string node) =>
        string.Join(", ", ScriptWrites.Settable.Select(member => $"{member.Name} = {node}.{member.Name}"));
}
