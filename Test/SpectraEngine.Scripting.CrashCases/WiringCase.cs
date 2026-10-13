using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.CrashCases;

// A script of a level that plays calls the inputs of an entity and hears
// its outputs, the right way and every wrong way. Each refusal is an error
// raised out of a host function, each call of a listener starts a thread
// from the level's queue, and every line the script prints runs the
// collector in the middle of it.
internal static class WiringCase
{
    private const int Rounds = 20;
    private const int Rings = 10;

    // What each wrong call must say. A script prints them in this order.
    private static readonly string[] _refusals =
    [
        "invalid argument #1 to 'Strike' (number expected, got string)",
        "too many arguments to 'Ring' (no value expected, got 1 value)",
        "Shatter is not a valid member of bell \"Bell\"",
        "OnShattered is not a valid member of bell \"Bell\"",
    ];

    internal static int Run()
    {
        string[] canaries = Stress.MakeCanaries();

        for (int round = 1; round <= Rounds; round++)
        {
            string? failure = Play() ?? Stress.FindDamage(canaries);
            if (failure is not null)
            {
                Console.WriteLine($"CASE failed in round {round}: {failure}");
                return CaseExit.Failed;
            }

            GC.Collect();
        }

        Console.WriteLine(
            $"CASE sent {Rounds * Rings * 3} inputs in a level, {Rounds * Rings * _refusals.Length} refused");
        return CaseExit.Passed;
    }

    // Null when the script was refused what it should be, heard what it sent and went on.
    private static string? Play()
    {
        Scene scene = new();
        SceneNode bell = scene.Root.CreateChild("Bell");
        bell.Entity = new EntityData(Bell.Class);
        bell.CreateChild("Ringer").Script = new Script("Scripts/ringer.luau");

        var catalog = new EntityCatalog();
        catalog.Add(Bell.Schema, static () => new Bell());

        Heard sink = new();
        EntityWorld world = new(scene, NullLogger.Instance, catalog);
        LuauScriptRuntime runtime = new(new ScriptRuntimeSetup(world, sink, new Files()));
        world.Scripts = runtime;
        try
        {
            // The script starts on the first tick, and that tick's queue
            // sends what it sent. The next one lets it print what it heard.
            world.Activate();
            for (int tick = 0; tick < 3; tick++)
                world.Tick(world.FixedDeltaTime);

            world.Deactivate();
        }
        finally
        {
            runtime.Close();
        }

        return Judge(sink);
    }

    private static string? Judge(Heard sink)
    {
        if (sink.Errors > 0)
            return $"{sink.Errors} errors were reported, and every wrong call was caught by the script";
        if (sink.Refused.Count != Rings * _refusals.Length)
            return $"the script was refused {sink.Refused.Count} times";

        for (int i = 0; i < sink.Refused.Count; i++)
        {
            if (!sink.Refused[i].EndsWith(_refusals[i % _refusals.Length], StringComparison.Ordinal))
                return $"a wrong call said '{sink.Refused[i]}'";
        }

        // Two rings a round, the forces 1 to 10, and the first force the waiting thread got.
        string wentOn = $"went on {Rings * 2} {Rings * (Rings + 1) / 2} 1";
        return sink.WentOn == wentOn ? null : $"the script ended with '{sink.WentOn}' and not '{wentOn}'";
    }

    private const string Ringer = """
        local bell = workspace.Bell
        local rung, struck = 0, 0
        bell.OnRung:Connect(function() rung += 1 end)
        bell.OnStruck:Connect(function(force) struck += force end)

        local function refused(action)
            print('refused ' .. tostring(select(2, pcall(action))))
        end

        for round = 1, ROUNDS do
            local ring = bell.Ring
            bell:Ring()
            ring(bell)
            bell:Strike(round)
            refused(function() bell:Strike('hard') end)
            refused(function() bell:Ring(1) end)
            refused(function() bell:Shatter() end)
            refused(function() return bell.OnShattered end)
        end

        local first = bell.OnStruck:Wait()
        task.wait()
        print('went on ' .. rung .. ' ' .. struck .. ' ' .. first)
        """;

    // Rings with no value and is struck with a force, and fires an output for each.
    private sealed class Bell : Entity
    {
        public const string Class = "bell";

        public static EntitySchema Schema { get; } = new(
            Class,
            inputs: [new("Ring", ParameterType.None), new("Strike", ParameterType.Of(KeyvalueType.Float))],
            outputs: [new("OnRung", ParameterType.None), new("OnStruck", ParameterType.Of(KeyvalueType.Float))]);

        public override bool AcceptInput(string input, ref EntityInputContext context)
        {
            Stress.Churn();
            switch (input)
            {
                case "Ring":
                    FireOutput("OnRung");
                    return true;
                case "Strike":
                    FireOutput("OnStruck", parameterOverride: context.Parameter);
                    return true;
                default:
                    return false;
            }
        }
    }

    private sealed class Files : IScriptSource
    {
        public byte[]? Read(string path, out string? problem)
        {
            problem = null;
            return Encoding.UTF8.GetBytes(Ringer.Replace("ROUNDS", Rings.ToString(), StringComparison.Ordinal));
        }
    }

    // Keeps what the script prints, and runs the collector on every line.
    private sealed class Heard : IScriptSink
    {
        public List<string> Refused { get; } = [];

        public string WentOn { get; private set; } = "";

        public int Errors { get; private set; }

        public void Report(in ScriptMessage message)
        {
            Stress.Churn();
            if (message.Severity == ScriptSeverity.Error)
                Errors++;
            else if (message.Text.StartsWith("refused ", StringComparison.Ordinal))
                Refused.Add(message.Text);
            else if (message.Text.StartsWith("went on ", StringComparison.Ordinal))
                WentOn = message.Text;
        }
    }
}
