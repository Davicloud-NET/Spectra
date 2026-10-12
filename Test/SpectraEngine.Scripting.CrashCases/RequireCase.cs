using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.CrashCases;

// A script of a level that plays asks for modules that end every way a
// module can. This is require itself, with the runtime a game has. Every
// print and every file that is read puts the collector to work, so it runs
// inside a module, inside the require that loads it, and in the asker.
internal static class RequireCase
{
    private const int Rounds = 20;
    private const string WentOn = "true went on";

    private static readonly TimeSpan _hangLimit = TimeSpan.FromMilliseconds(50);

    // What the script asks for, whether it gets it, and a part of what it gets.
    private static readonly (string Module, bool Works, string Gives)[] _asks =
    [
        ("'./ways/returns'", true, "42"),
        ("'./ways/returns'", true, "42"),
        ("'./ways/fails'", false, "ways/fails.luau:1: attempt to index nil"),
        ("'./ways/fails'", false, "ways/fails.luau:1: attempt to index nil"),
        ("'./ways/waits'", false, "ways/waits.luau:1: a module cannot wait"),
        ("'./ways/circle'", false, "required recursively"),
        ("'./ways/chain'", false, "ways/fails.luau:1: attempt to index nil"),
        ("'./ways/lost'", false, "no module at"),
        ("script.Parent", true, "Rules"),
        ("'./ways/never'", false, "ways/never.luau:1: "),
    ];

    // The module that fails, the one that waits, the circle and the one that is stopped.
    private const int ErrorsInARound = 4;

    internal static int Run()
    {
        string[] canaries = Stress.MakeCanaries();
        int errors = 0;

        for (int round = 1; round <= Rounds; round++)
        {
            string? failure = Play(out int reported) ?? Stress.FindDamage(canaries);
            if (failure is not null)
            {
                Console.WriteLine($"CASE failed in round {round}: {failure}");
                return CaseExit.Failed;
            }

            errors += reported;
            GC.Collect();
        }

        Console.WriteLine($"CASE asked for {Rounds * _asks.Length} modules in a level, {errors} errors reported");
        return errors == Rounds * ErrorsInARound ? CaseExit.Passed : CaseExit.Failed;
    }

    // Null when the script got from each ask what it should, and went on.
    private static string? Play(out int errors)
    {
        Scene scene = new();
        SceneNode rules = scene.Root.CreateChild("Rules");
        rules.Script = new Script("Scripts/rules.luau") { IsModule = true };
        rules.CreateChild("Asker").Script = new Script("Scripts/asker.luau");

        Heard sink = new();
        EntityWorld world = new(scene, NullLogger.Instance);
        LuauScriptRuntime runtime = new(new ScriptRuntimeSetup(world, sink, new Files()));
        sink.Runtime = runtime;
        world.Scripts = runtime;
        try
        {
            // The scripts start on the first tick. The later ones wake what waited.
            world.Activate();
            for (int tick = 0; tick < 3; tick++)
                world.Tick(world.FixedDeltaTime);

            world.Deactivate();
        }
        finally
        {
            runtime.Close();
        }

        errors = sink.Errors;
        return Judge(sink.Results);
    }

    private static string? Judge(List<string> results)
    {
        if (results.Count != _asks.Length + 1 || results[^1] != WentOn)
            return $"the script printed {results.Count} results and did not go on to its end";

        for (int i = 0; i < _asks.Length; i++)
        {
            (string module, bool works, string gives) = _asks[i];
            bool isRight = results[i].StartsWith(works ? "true " : "false ", StringComparison.Ordinal)
                && results[i].Contains(gives, StringComparison.Ordinal);
            if (!isRight)
                return $"require({module}) gave '{results[i]}'";
        }

        return null;
    }

    private static string Asker()
    {
        StringBuilder text = new("""
            local function ask(what)
                print('asks')
                local worked, got = pcall(require, what)
                print(tostring(worked) .. ' ' .. tostring(got))
            end

            """);

        foreach ((string module, _, _) in _asks)
            text.Append("ask(").Append(module).Append(")\n");

        return text.Append("print('").Append(WentOn).Append("')\n").ToString();
    }

    // The level's files. Reading one runs the collector, inside the require that asked.
    private sealed class Files : IScriptSource
    {
        private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal)
        {
            ["Scripts/asker.luau"] = Asker(),
            ["Scripts/rules.luau"] = "print('in rules') return script.Name",
            ["Scripts/ways/returns.luau"] = "print('in returns') return 42",
            ["Scripts/ways/fails.luau"] = "print('in fails') local door = nil return door.open",
            ["Scripts/ways/waits.luau"] = "print('in waits') task.wait() return 1",
            ["Scripts/ways/circle.luau"] = "print('in circle') return require('./circle')",
            ["Scripts/ways/chain.luau"] = "print('in chain') return require('./fails')",
            ["Scripts/ways/never.luau"] = $"print('{Heard.Hangs}') while true do end",
        };

        public byte[]? Read(string path, out string? problem)
        {
            Stress.Churn();
            problem = null;
            return _texts.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null;
        }
    }

    // Keeps the results the script prints, and runs the collector on every line.
    private sealed class Heard : IScriptSink
    {
        public const string Hangs = "in never";

        public LuauScriptRuntime? Runtime { get; set; }

        public List<string> Results { get; } = [];

        public int Errors { get; private set; }

        public void Report(in ScriptMessage message)
        {
            Stress.Churn();
            if (message.Severity == ScriptSeverity.Error)
            {
                Errors++;
                SetLimit(LuauHost.DefaultCallLimit);
            }
            else if (message.Text == Hangs)
            {
                // Only the module that hangs runs against a short limit, so a
                // collection that takes long stops none of the others.
                SetLimit(_hangLimit);
            }
            else if (message.Text.StartsWith("true ", StringComparison.Ordinal)
                || message.Text.StartsWith("false ", StringComparison.Ordinal))
            {
                Results.Add(message.Text);
            }
        }

        private void SetLimit(TimeSpan limit)
        {
            if (Runtime is { } runtime)
                runtime.CallLimit = limit;
        }
    }
}
