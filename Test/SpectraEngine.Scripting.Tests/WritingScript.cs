using System.Globalization;
using System.Text;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// The Luau of a script that makes random writes: to its own node, to the
// node above it and to the root, a few each tick. Every write is in a pcall,
// and the script ends by printing how many were taken and how many refused.
internal static class WritingScript
{
    private const int WritesPerTick = 4;

    private const string TallyWord = "tally";

    // Indexes into the script's table of targets.
    private const int TargetCount = 3;

    public static string From(RandomWrites random, int writes)
    {
        var text = new StringBuilder()
            .AppendLine("local targets = { script, script.Parent, workspace }")
            .AppendLine("local atStart = {}")
            .AppendLine("for i, target in targets do")
            .AppendLine($"    atStart[i] = {{ {MembersOf("target")} }}")
            .AppendLine("end")
            .AppendLine("local taken, refused = 0, 0")
            .AppendLine("local function try(write)")
            .AppendLine("    if pcall(write) then taken += 1 else refused += 1 end")
            .AppendLine("end");

        for (int i = 0; i < writes; i++)
        {
            RandomWrite write = random.Next();
            int target = 1 + random.Pick(TargetCount);
            string member = write.Member.Name;
            string value = write.PutsBack ? $"atStart[{target}].{member}" : write.Luau;
            text.AppendLine(
                CultureInfo.InvariantCulture, $"try(function() targets[{target}].{member} = {value} end)");

            if (i % WritesPerTick == WritesPerTick - 1)
                text.AppendLine("task.wait()");
        }

        return text.AppendLine($"print('{TallyWord}', taken, refused)").ToString();
    }

    // The ticks a script of so many writes needs to reach its last line.
    public static int TicksFor(int writes) => (writes / WritesPerTick) + 2;

    // What the scripts that have ended counted, summed.
    public static (int Scripts, int Taken, int Refused) Tally(IEnumerable<string> printed)
    {
        int scripts = 0;
        int taken = 0;
        int refused = 0;
        foreach (string line in printed)
        {
            string[] parts = line.Split(' ');
            if (parts.Length != 3 || parts[0] != TallyWord)
                continue;

            scripts++;
            taken += int.Parse(parts[1], CultureInfo.InvariantCulture);
            refused += int.Parse(parts[2], CultureInfo.InvariantCulture);
        }

        return (scripts, taken, refused);
    }

    // Every member a script can set, read from one node: "Name = node.Name, ...".
    private static string MembersOf(string node) =>
        string.Join(", ", ScriptWrites.Settable.Select(member => $"{member.Name} = {node}.{member.Name}"));
}
