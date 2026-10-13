using System.Numerics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.CrashCases;

// A script of a level makes, clones, moves and destroys nodes, and is refused
// every way those are refused. A refusal is an error raised from a host
// function, a destroyed node ends threads and connections from inside one,
// and a clone's script starts from the tick. Stop then puts the tree back.
internal static class TreeCase
{
    private const int Rounds = 20;
    private const int Ticks = 12;
    private const string WentOn = "true went on";

    // What the script does, whether it is taken, and a part of what it gets.
    private static readonly (string Luau, bool Works, string Gives)[] _asks =
    [
        ("local made = Instance.new('Part') return made.ClassName .. tostring(made.Parent)", true, "Partnil"),
        ("local made = Instance.new('PointLight', crate) return made.Parent.Name .. made.Range", true, "Crate8"),
        ("return Instance.new('Model')", false, "Unable to create an Instance of type \"Model\""),
        ("return Instance.new()", false, "string expected, got nil"),
        ("return Instance.new('Part', 5)", false, "Instance expected, got number"),
        ("local copy = crate:Clone() copy.Name = 'Copy' copy.Parent = workspace return workspace.Copy == copy", true, "true"),
        ("return workspace.Copy.PointLight ~= crate.PointLight", true, "true"),
        ("return wall:Clone()", false, "cannot be cloned: a world brush"),
        ("return workspace:Clone()", false, "is the root of the level and cannot be cloned"),
        ("wall:Destroy()", false, "cannot be destroyed: a world brush"),
        ("wall.Parent = crate", false, "cannot get another parent: a world brush"),
        ("crate.Parent = workspace.Copy.PointLight return crate.Parent.Parent.Name", true, "Copy"),
        ("workspace.Copy.Parent = crate", false, "would result in circular reference"),
        ("crate.Parent = 5", false, "Instance expected, got number"),
        ("workspace.Parent = crate", false, "is locked: it is the root of the level"),
        ("crate.Parent = nil return tostring(crate:IsDescendantOf(game)) .. tostring(crate.Parent)", true, "falsenil"),
        ("crate.Parent = workspace return crate:IsDescendantOf(game)", true, "true"),

        // A script and an entity under a clone start at the end of the tick.
        ("local twin = scripted:Clone() twin.Parent = workspace task.wait() return workspace:GetAttribute('Ran')", true, "2"),
        ("local other = sign:Clone() other.Parent = workspace task.wait() return other.ClassName", true, "sign"),

        // A destroyed node ends the thread that waits on it and the
        // connection to its touch, from inside the call that destroys it.
        ("local gone = Instance.new('Part', workspace) local touched = gone.Touched:Connect(function() end) " +
            "task.spawn(function() gone.Touched:Wait() end) task.spawn(function() gone:WaitForChild('Never') end) " +
            "gone:Destroy() return tostring(touched.Connected) .. tostring(gone.Parent)", true, "falsenil"),
        ("local gone = Instance.new('Folder', workspace) gone:Destroy() gone.Parent = workspace", false, "is locked: Folder was destroyed"),
        ("local gone = Instance.new('Folder') gone:Destroy() gone:AddTag('late')", false, "a destroyed node holds no tag"),
        ("workspace.Copy:Destroy() return #workspace.Copy:GetChildren()", false, "Copy is not a valid member"),

        // Enough nodes nobody keeps to have them looked over, which runs a
        // full collection of Luau from inside a host function.
        ("for i = 1, 300 do Instance.new('Folder'):AddTag('loose') end return 'kept none'", true, "kept none"),
        ("for i = 1, 100 do local made = crate:Clone() made.Parent = workspace made:Destroy() end return 'gone'", true, "gone"),
        ("local deep = workspace for i = 1, 300 do deep = Instance.new('Folder', deep) end", false, "nodes deep"),

        // What a script holds of a destroyed node: a signal that takes no
        // wait, a wait that ends its thread, and nodes that count on.
        ("local gone = Instance.new('Part') local touched = gone.Touched gone:Destroy() " +
            "touched:Connect(function() end) touched:Wait()", false, "will not return: 'Part' was destroyed"),
        ("local gone = Instance.new('Folder') gone:Destroy() task.spawn(function() gone:WaitForChild('Never') end) " +
            "task.wait() return 'ended'", true, "ended"),
        ("local kept = {} for i = 1, 20000 do local made = Instance.new('Folder') made:Destroy() kept[i] = made end",
            false, "still there or still held"),
    ];

    internal static int Run()
    {
        string[] canaries = Stress.MakeCanaries();
        int refused = 0;

        for (int round = 1; round <= Rounds; round++)
        {
            string? failure = Play(out int refusals) ?? Stress.FindDamage(canaries);
            if (failure is not null)
            {
                Console.WriteLine($"CASE failed in round {round}: {failure}");
                return CaseExit.Failed;
            }

            refused += refusals;
            GC.Collect();
        }

        Console.WriteLine($"CASE made, cloned and destroyed {Rounds * _asks.Length} times in a level, {refused} refused");
        return refused == Rounds * _asks.Count(ask => !ask.Works) ? CaseExit.Passed : CaseExit.Failed;
    }

    // Null when the script got from each ask what it should, went on, and
    // Stop left the level's tree as it was built.
    private static string? Play(out int refusals)
    {
        Scene scene = new();
        scene.Root.CreateChild("Wall").Brush = Brush.CreateBox(new Vector3(-4f, 0f, -0.5f), new Vector3(4f, 3f, 0.5f));
        SceneNode crate = Part(scene.Root, "Crate");
        crate.CreateChild("PointLight").Light = new Light { Kind = LightKind.Point };
        Part(scene.Root, "Scripted").CreateChild("Counts").Script = new Script("Scripts/counts.luau");
        scene.Root.CreateChild("Sign").Entity = new EntityData(Sign.Class);
        scene.Root.CreateChild("Asker").Script = new Script("Scripts/asker.luau");
        string built = Describe(scene);

        Heard sink = new();
        EntityWorld world = new(scene, NullLogger.Instance, Sign.Classes());
        LuauScriptRuntime runtime = new(new ScriptRuntimeSetup(world, sink, new Files()));
        world.Scripts = runtime;
        try
        {
            world.Activate();
            for (int tick = 0; tick < Ticks; tick++)
                world.Tick(world.FixedDeltaTime);

            world.Deactivate();
        }
        finally
        {
            runtime.Close();
        }

        refusals = sink.Results.Count(result => result.StartsWith("false ", StringComparison.Ordinal));
        if (sink.Errors > 0)
            return $"{sink.Errors} errors left the script";
        if (Describe(scene) != built)
            return $"Stop left the tree as '{Describe(scene)}', and it was built as '{built}'";

        return Judge(sink.Results);
    }

    private static string? Judge(List<string> results)
    {
        if (results.Count != _asks.Length + 1 || results[^1] != WentOn)
            return $"the script printed {results.Count} results and did not go on to its end";

        for (int i = 0; i < _asks.Length; i++)
        {
            (string luau, bool works, string gives) = _asks[i];
            bool isRight = results[i].StartsWith(works ? "true " : "false ", StringComparison.Ordinal)
                && results[i].Contains(gives, StringComparison.Ordinal);
            if (!isRight)
                return $"'{luau}' gave '{results[i]}'";
        }

        return null;
    }

    // Every node with the node above it and where it is, in the tree's order.
    private static string Describe(Scene scene)
    {
        StringBuilder text = new();
        foreach (SceneNode node in scene.Root.Traverse())
            text.Append(node.Parent?.Name).Append('/').Append(node.Name).Append('@').Append(node.LocalPosition).Append(' ');

        return text.ToString();
    }

    private static SceneNode Part(SceneNode parent, string name)
    {
        SceneNode node = parent.CreateChild(name);
        node.LocalPosition = new Vector3(0f, 2f, 6f);
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));
        return node;
    }

    private static string Asker()
    {
        StringBuilder text = new("""
            local wall, crate, scripted, sign = workspace.Wall, workspace.Crate, workspace.Scripted, workspace.Sign
            local function ask(what)
                print('asks')
                local worked, got = pcall(what)
                print(tostring(worked) .. ' ' .. tostring(got))
            end

            """);

        foreach ((string luau, _, _) in _asks)
            text.Append("ask(function() ").Append(luau).Append(" end)\n");

        return text.Append("print('").Append(WentOn).Append("')\n").ToString();
    }

    // An entity that does nothing.
    private sealed class Sign : Entity
    {
        public const string Class = "sign";

        public static EntityCatalog Classes()
        {
            EntityCatalog catalog = new();
            catalog.Add(new EntitySchema(Class), static () => new Sign());
            return catalog;
        }
    }

    private sealed class Files : IScriptSource
    {
        // Each run of it counts one up on the root, so a script can tell
        // that the copy under a clone has started.
        private const string Counts = "workspace:SetAttribute('Ran', (workspace:GetAttribute('Ran') or 0) + 1)";

        public byte[]? Read(string path, out string? problem)
        {
            Stress.Churn();
            problem = null;
            return path switch
            {
                "Scripts/asker.luau" => Encoding.UTF8.GetBytes(Asker()),
                "Scripts/counts.luau" => Encoding.UTF8.GetBytes(Counts),
                _ => null,
            };
        }
    }

    // Keeps the results the script prints, and runs the collector on every line.
    private sealed class Heard : IScriptSink
    {
        public List<string> Results { get; } = [];

        public int Errors { get; private set; }

        public void Report(in ScriptMessage message)
        {
            Stress.Churn();
            if (message.Severity == ScriptSeverity.Error)
                Errors++;
            else if (message.Text.StartsWith("true ", StringComparison.Ordinal)
                || message.Text.StartsWith("false ", StringComparison.Ordinal))
                Results.Add(message.Text);
        }
    }
}
