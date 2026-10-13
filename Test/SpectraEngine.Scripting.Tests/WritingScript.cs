using System.Globalization;
using System.Text;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// The Luau of a script that writes at random to its own node, the node above
// it and the root: first every member of each, then a few a tick, each in a
// pcall. It prints its counts as one line, since all scripts end on one tick
// and may print only so many. A tick later it makes, clones, moves and destroys.
internal static class WritingScript
{
    // What every node that carries one of these scripts is called.
    public const string WriterName = "Writer";

    // The level's world brush, which never moves and so is a parent every
    // node that can move takes.
    public const string Home = "Baseplate";

    // The ticks a script's changes to the tree take, with the ticks after
    // them in which what came into the level starts.
    public const int TreeTicks = 3;

    private const int WritesPerTick = 4;

    private const int TreeStepsPerScript = 6;

    private const string EndWord = "refused";

    private static readonly string[] MadeClasses = ["Part", "Folder", "PointLight", "SpotLight", "DirectionalLight"];

    // Indexes into the script's table of targets.
    private const int TargetCount = 3;

    // What a script node has of the members: those every node has.
    private static readonly SceneNode AnyNode = new("Any");

    private static int SweepWrites => TargetCount * ScriptWrites.Settable.Count;

    // The script for a node, which it will sit under. Most of its random
    // writes are to members its targets have.
    public static string From(RandomWrites random, int writes, SceneNode parent)
    {
        SceneNode root = parent;
        while (root.Parent is { } above)
            root = above;

        SceneNode[] targets = [AnyNode, parent, root];

        // The wait comes before any script's first write, so each script read
        // what the level was authored with. A member the target does not have
        // is left out. The parents a script can name are the nodes the level
        // was built with, in the order of its tree, as RandomWrites counts them.
        var text = new StringBuilder()
            .AppendLine("local all = { workspace }")
            .AppendLine("for _, node in workspace:GetDescendants() do")
            .AppendLine($"    if node.Name ~= '{WriterName}' then table.insert(all, node) end")
            .AppendLine("end")
            .AppendLine($"local homes = {{ workspace, workspace:FindFirstChild('{Home}') }}")
            .AppendLine("local targets = { script, script.Parent, workspace }")
            .AppendLine($"local members = {{ {string.Join(", ", ScriptWrites.Settable.Select(member => $"'{member.Name}'"))} }}")
            .AppendLine("local atStart = {}")
            .AppendLine("for i, target in targets do")
            .AppendLine("    local held = {}")
            .AppendLine("    for _, member in members do")
            .AppendLine("        local has, value = pcall(function() return target[member] end)")
            .AppendLine("        if has then held[member] = value end")
            .AppendLine("    end")
            .AppendLine("    atStart[i] = held")
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
                Append(text, member, Changing(random, member, target), target, ref written);
        }

        // The random writes start on a tick of their own. One that puts a
        // value back would else hide a write of the same tick from a look.
        text.AppendLine("task.wait()");
        for (int i = 0; i < writes; i++)
        {
            int target = 1 + random.Pick(TargetCount);
            RandomWrite write = random.Next(targets[target - 1]);
            string value = write.PutsBack ? $"atStart[{target}].{write.Member.Name}" : write.Luau;
            Append(text, write.Member, value, target, ref written);
        }

        text.AppendLine("local counts = {}")
            .AppendLine("for member, count in taken do table.insert(counts, `{member}={count}`) end")
            .AppendLine($"print('{EndWord}', refused, table.concat(counts, ' '))");

        AppendTreeSteps(text, random);
        return text.ToString();
    }

    // The ticks a script of so many random writes needs to reach its last line.
    public static int TicksFor(int writes) => 4 + ((SweepWrites + writes) / WritesPerTick);

    // After it has printed what it counted, a script changes the tree on a
    // tick of its own: it makes nodes, clones, moves and destroys, all at
    // once. One of them may end the script itself, or another one that has
    // not come to this yet. Each step is in a pcall, since many are refused.
    private static void AppendTreeSteps(StringBuilder text, RandomWrites random)
    {
        text.AppendLine("task.wait()")
            .AppendLine("local made = {}")
            .AppendLine("local function keep(node) table.insert(made, node) return node end");

        for (int i = 0; i < TreeStepsPerScript; i++)
        {
            string any = $"all[{1 + random.Pick(random.Nodes.Count)}]";
            string other = $"all[{1 + random.Pick(random.Nodes.Count)}]";
            string step = random.Pick(8) switch
            {
                0 or 1 => $"keep(Instance.new('{MadeClasses[random.Pick(MadeClasses.Length)]}')).Parent = {any}",
                2 or 3 => $"keep({any}:Clone()).Parent = {other}",
                4 => "made[#made]:Destroy()",
                5 => $"{any}:Destroy()",
                6 => $"{any}.Parent = {other}",
                _ => $"{any}.Parent = nil",
            };

            text.AppendLine(CultureInfo.InvariantCulture, $"pcall(function() {step} end)");
        }
    }

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

    // A value the member takes that is not the one it has. A switch has two
    // values and a random one would leave half of them as they are, so it is
    // turned over, and a number is moved on by one.
    private static string Changing(RandomWrites random, SettableMember member, int target) => member.Kind switch
    {
        MemberValueKind.Boolean => $"not atStart[{target}].{member.Name}",
        MemberValueKind.Number => $"(atStart[{target}].{member.Name} or 0) + 1",
        MemberValueKind.Node => $"(atStart[{target}].{member.Name} == homes[1]) and homes[2] or homes[1]",
        _ => random.Taken(member).Luau,
    };

    private static void Append(StringBuilder text, SettableMember member, string value, int target, ref int written)
    {
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"try('{member.Name}', function() targets[{target}].{member.Name} = {value} end)");

        if (++written % WritesPerTick == 0)
            text.AppendLine("task.wait()");
    }
}
