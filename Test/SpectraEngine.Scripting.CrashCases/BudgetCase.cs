using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.CrashCases;

// Scripts of a level that set long attributes, give many tags and clone
// past the memory budget, each from inside a pcall. A host function finds
// the budget gone, looks for room with a full collection that lets loose
// trees go, fails, and the stop raises again at the script's next call.
internal static class BudgetCase
{
    private const int Rounds = 10;
    private const long Budget = 4 * 1024 * 1024;
    private const string Sentence = "with the tags and attributes they set on nodes, and this one was switched off";

    private static readonly string[] _scripts =
    [
        """
        local blob = string.rep("x", 65536)
        while true do
            pcall(function()
                local part = Instance.new("Part")
                part.Parent = workspace
                part:SetAttribute("Blob", blob)
                local loose = Instance.new("Part")
                loose:SetAttribute("Blob", blob)
            end)
        end
        """,
        """
        local stem = string.rep("t", 200)
        while true do
            local part = Instance.new("Part")
            part.Parent = workspace
            for index = 1, 256 do
                pcall(part.AddTag, part, stem .. index)
            end
        end
        """,
        """
        local model = Instance.new("Folder")
        for index = 1, 20 do
            local part = Instance.new("Part")
            part:SetAttribute("Blob", string.rep("y", 65536))
            part.Parent = model
        end
        model.Parent = workspace
        local copies = {}
        while true do
            local worked, copy = pcall(model.Clone, model)
            copies[#copies + 1] = copy
        end
        """,
    ];

    internal static int Run()
    {
        string[] canaries = Stress.MakeCanaries();
        int stopped = 0;

        for (int round = 1; round <= Rounds; round++)
        {
            foreach (string script in _scripts)
            {
                string? failure = Play(script) ?? Stress.FindDamage(canaries);
                if (failure is not null)
                {
                    Console.WriteLine($"CASE failed in round {round}: {failure}");
                    return CaseExit.Failed;
                }

                stopped++;
            }

            GC.Collect();
        }

        Console.WriteLine($"CASE stopped {stopped} scripts at the budget in a level");
        return CaseExit.Passed;
    }

    // Null when the script was switched off with the sentence, the count of
    // what scripts set stayed under the budget and matched a recount, and
    // nothing was counted once the level stopped.
    private static string? Play(string text)
    {
        Scene scene = new();
        scene.Root.CreateChild("Hoarder").Script = new Script("Scripts/hoarder.luau");

        Heard sink = new();
        EntityWorld world = new(scene, NullLogger.Instance, new EntityCatalog());
        LuauScriptRuntime runtime = new(new ScriptRuntimeSetup(world, sink, new Files(text)));
        runtime.MemoryBudget = Budget;
        world.Scripts = runtime;
        string? failure = null;
        try
        {
            world.Activate();
            world.Tick(world.FixedDeltaTime);
            world.Tick(world.FixedDeltaTime);

            runtime.Collect();
            if (sink.Errors.Count != 1 || !sink.Errors[0].EndsWith(Sentence, StringComparison.Ordinal))
                failure = $"the script ended with {sink.Errors.Count} errors: {string.Join(" | ", sink.Errors)}";
            else if (runtime.NodeBytes + runtime.LuauBytes > Budget)
                failure = $"the scripts kept {runtime.NodeBytes + runtime.LuauBytes} bytes";
            else if (runtime.NodeBytes != runtime.CountNodeBytes())
                failure = "the count of what scripts set drifted";

            world.Deactivate();
        }
        finally
        {
            runtime.Close();
        }

        return failure ?? (runtime.NodeBytes != 0 ? "something was counted after Stop" : null);
    }

    private sealed class Files(string text) : IScriptSource
    {
        public byte[]? Read(string path, out string? problem)
        {
            Stress.Churn();
            problem = null;
            return path == "Scripts/hoarder.luau" ? Encoding.UTF8.GetBytes(text) : null;
        }
    }

    // Keeps the errors, and runs the collector on every line.
    private sealed class Heard : IScriptSink
    {
        public List<string> Errors { get; } = [];

        public void Report(in ScriptMessage message)
        {
            Stress.Churn();
            if (message.Severity == ScriptSeverity.Error)
                Errors.Add(message.Text);
        }
    }
}
