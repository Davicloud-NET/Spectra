using System.Numerics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.CrashCases;

// A script of a level that plays sets and reads what a light, a part and an
// entity have, and is refused every way the door refuses. A refusal is an
// error raised from a host function, and every print puts the collector to
// work. Stop then has to put the level back.
internal static class MemberCase
{
    private const int Rounds = 20;
    private const string WentOn = "true went on";

    // What the script does, whether it is taken, and a part of what it gets.
    private static readonly (string Luau, bool Works, string Gives)[] _asks =
    [
        ("lamp.Enabled = false return lamp.Enabled", true, "false"),
        ("lamp.Brightness = 2.5 return lamp.Brightness", true, "2.5"),
        ("lamp.Color = Color3.fromRGB(255, 128, 0) return lamp.Color:ToHex()", true, "ff8000"),
        ("lamp.Range += 1 return lamp.Range", true, "11"),
        ("return lamp.Color:Lerp(Color3.new(), 0.5)", true, "0.5, 0.25098, 0"),
        ("return lamp.Name .. lamp.ClassName", true, "LampPointLight"),
        ("lamp.Brightness = -1", false, "Brightness cannot be -1"),
        ("lamp.Color = Color3.new(0/0, 0, 0)", false, "Color cannot be NaN, 0, 0"),
        ("lamp.Enabled = 1", false, "boolean expected, got number"),
        ("crate.CanCollide = false return crate.CanCollide", true, "false"),
        ("crate.Visible = false return crate.Visible", true, "false"),
        ("crate.Brightness = 1", false, "Brightness is not a valid member of Part"),
        ("wall.CanTouch = false", false, "it is a world brush"),
        ("return sign:GetAttribute('speed')", true, "1.5"),
        ("return sign:GetAttribute('tint')", true, "1, 1, 1"),
        ("return sign:GetAttribute('nothing')", true, "nil"),
        ("return Color3.fromHex('nope')", false, "Color3.fromHex takes"),
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

        Console.WriteLine($"CASE set and read {Rounds * _asks.Length} members in a level, {refused} refused");
        return refused == Rounds * _asks.Count(ask => !ask.Works) ? CaseExit.Passed : CaseExit.Failed;
    }

    // Null when the script got from each ask what it should, went on, and
    // the level is as it was built once it has stopped.
    private static string? Play(out int refusals)
    {
        Scene scene = new();
        SceneNode lamp = scene.Root.CreateChild("Lamp");
        Light light = lamp.Light = new Light { Kind = LightKind.Point };
        SceneNode crate = scene.Root.CreateChild("Crate");
        crate.BrushKind = BrushKind.Part;
        crate.Brush = Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));
        scene.Root.CreateChild("Wall").Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
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
            world.Deactivate();
        }
        finally
        {
            runtime.Close();
        }

        refusals = sink.Results.Count(result => result.StartsWith("false ", StringComparison.Ordinal));
        if (sink.Errors > 0)
            return $"{sink.Errors} errors left the script";

        bool isBack = light is { Enabled: true, Intensity: 1f, Range: 10f } && light.Color == Vector3.One
            && crate is { CanCollide: true, IsRendered: true };
        return isBack ? Judge(sink.Results) : "Stop did not put the level back";
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
            local lamp, crate, wall, sign = workspace.Lamp, workspace.Crate, workspace.Wall, workspace.Sign
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
