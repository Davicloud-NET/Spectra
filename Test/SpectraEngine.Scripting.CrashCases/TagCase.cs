using System.Numerics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.CrashCases;

// A script of a level that plays tags nodes, sets attributes of every kind
// and hears of both, and is refused every way a tag and an attribute are
// refused. A refusal is an error raised from a host function, a listener is
// called from the level's queue, and every print puts the collector to work.
internal static class TagCase
{
    private const int Rounds = 20;
    private const string WentOn = "true went on";

    // What the script does, whether it is taken, and a part of what it gets.
    private static readonly (string Luau, bool Works, string Gives)[] _asks =
    [
        ("crate:AddTag('Heavy') return crate:HasTag('Heavy')", true, "true"),
        ("lamp:AddTag('Heavy') return #tags:GetTagged('Heavy')", true, "2"),
        ("return tags:GetTagged('Heavy')[1].Name .. tags:GetTagged('Heavy')[2].Name", true, "LampCrate"),
        ("crate:AddTag('Crate') return table.concat(crate:GetTags(), ' ')", true, "Crate Heavy"),
        ("tags:RemoveTag(lamp, 'Heavy') return #tags:GetTagged('Heavy') + #tags:GetTagged('Nobody')", true, "1"),
        ("crate:AddTag(7)", false, "string expected, got number"),
        ("tags:AddTag('Heavy', crate)", false, "Instance expected, got string"),
        ("crate:AddTag('\\255')", false, "not valid UTF-8"),
        ("crate:SetAttribute('Weight', 40) return crate:GetAttribute('Weight')", true, "40"),
        ("crate:SetAttribute('Rest', CFrame.new(1, 2, 3)) return crate:GetAttribute('Rest').Position", true, "1, 2, 3"),
        ("crate:SetAttribute('Glow', Color3.fromRGB(255, 128, 0)) return crate:GetAttribute('Glow'):ToHex()", true, "ff8000"),
        ("crate:SetAttribute('Label', 'this side up') return crate:GetAttribute('Label')", true, "this side up"),
        ("crate:SetAttribute('Weight', nil) return crate:GetAttribute('Weight')", true, "nil"),
        ("sign:SetAttribute('speed', 9) return sign:GetAttribute('speed')", true, "9"),
        ("local all = sign:GetAttributes() return tostring(all.speed) .. tostring(all.tint)", true, "91, 1, 1"),
        ("sign:SetAttribute('ui/hud.title-2', 3) return sign:GetAttributes()['ui/hud.title-2']", true, "3"),
        ("crate:SetAttribute('not a name', 1)", false, "is not a valid attribute name"),
        ("crate:SetAttribute('Owner', workspace)", false, "Instance is not a supported attribute type"),
        ("crate:SetAttribute('Text', '\\255')", false, "not valid UTF-8"),
        ("crate:SetAttribute()", false, "string expected, got nil"),

        // Enough tags nobody keeps to have them looked over, which runs a
        // full collection of Luau from inside a host function.
        ("for i = 1, 200 do tags:GetInstanceAddedSignal('passing' .. i) end return #tags:GetTagged('Heavy')", true, "1"),
        ("crate:AddTag(string.rep('t', 257))", false, "a tag is at most 256 bytes long"),
        ("crate:SetAttribute('Note', string.rep('x', 65537))", false, "is at most 65536 bytes long"),
        ("return crate:GetAttributeChangedSignal('not a name') == lamp:GetAttributeChangedSignal('\\255')", true, "true"),
        ("task.wait() return heard", true, "+Crate +Lamp -Lamp Weight Rest Glow Label Weight -Crate"),
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

        Console.WriteLine($"CASE tagged and set {Rounds * _asks.Length} times in a level, {refused} refused");
        return refused == Rounds * _asks.Count(ask => !ask.Works) ? CaseExit.Passed : CaseExit.Failed;
    }

    // Null when the script got from each ask what it should, went on, and
    // the runtime holds nothing once the level has stopped.
    private static string? Play(out int refusals)
    {
        Scene scene = new();
        scene.Root.CreateChild("Lamp").Light = new Light { Kind = LightKind.Point };
        SceneNode crate = scene.Root.CreateChild("Crate");
        crate.BrushKind = BrushKind.Part;
        crate.Brush = Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));
        scene.Root.CreateChild("Sign").Entity = new EntityData(Sign.Class);
        scene.Root.CreateChild("Asker").Script = new Script("Scripts/asker.luau");

        Heard sink = new();
        EntityWorld world = new(scene, NullLogger.Instance, Sign.Classes());
        LuauScriptRuntime runtime = new(new ScriptRuntimeSetup(world, sink, new Files()));
        world.Scripts = runtime;
        try
        {
            world.Activate();
            world.Tick(world.FixedDeltaTime);

            // A tagged node leaves in the middle of a level.
            scene.Root.RemoveChild(crate);
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
        if (runtime.TaggedNodeCount + runtime.AttributeNodeCount > 0)
            return "the runtime holds a node after Stop";

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

    private static string Asker()
    {
        StringBuilder text = new("""
            local tags = game:GetService("CollectionService")
            local lamp, crate, sign = workspace.Lamp, workspace.Crate, workspace.Sign
            local heard = ""
            tags:GetInstanceAddedSignal("Heavy"):Connect(function(node) heard ..= "+" .. node.Name .. " " end)
            tags:GetInstanceRemovedSignal("Heavy"):Connect(function(node) heard ..= "-" .. node.Name .. " " end)
            crate.AttributeChanged:Connect(function(name) heard ..= name .. " " end)
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

    // An entity that does nothing, of a class that types two keyvalues.
    private sealed class Sign : Entity
    {
        public const string Class = "sign";

        public static EntityCatalog Classes()
        {
            EntityCatalog catalog = new();
            catalog.Add(
                new EntitySchema(Class, keyvalues:
                [
                    Declared("speed", KeyvalueType.Float, "1.5"),
                    Declared("tint", KeyvalueType.Color, "1 1 1"),
                ]),
                static () => new Sign());
            return catalog;
        }

        private static KeyvalueDescriptor Declared(string name, KeyvalueType type, string value) =>
            new(name, "", "", value, type, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u, KeyvalueDescriptor.NoChoices);
    }

    private sealed class Files : IScriptSource
    {
        public byte[]? Read(string path, out string? problem)
        {
            Stress.Churn();
            problem = null;
            return path == "Scripts/asker.luau" ? Encoding.UTF8.GetBytes(Asker()) : null;
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
