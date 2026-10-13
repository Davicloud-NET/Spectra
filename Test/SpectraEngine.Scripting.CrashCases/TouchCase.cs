using System.Numerics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.CrashCases;

// A kill brick in a level that plays, with the real mover walking onto it.
// The listener is handed the character and sends it back to the start, over
// and over. Listening to a world brush is refused, which is an error raised
// out of a host function, and every printed line runs the collector.
internal static class TouchCase
{
    private const int Rounds = 10;
    private const int Asks = 6;
    private const int Ticks = 600;
    private const int FewestKills = 6;

    // What each wrong way to listen must say. A script prints them in this order.
    private static readonly string[] _refusals =
    [
        "Wall cannot tell what touches it: it is a world brush",
        "Wall cannot tell what touches it: it is a world brush",
        "invalid argument #1 to 'Connect' (function expected, got number)",
        "Unable to assign property Touched. Property is read only",
    ];

    internal static int Run()
    {
        string[] canaries = Stress.MakeCanaries();
        string? first = null;

        for (int round = 1; round <= Rounds; round++)
        {
            string? failure = Play(out string wentOn) ?? Stress.FindDamage(canaries);

            // The same walk kills the player as often in every round.
            first ??= wentOn;
            if (failure is null && wentOn != first)
                failure = $"this round ended with '{wentOn}' and the first with '{first}'";

            if (failure is not null)
            {
                Console.WriteLine($"CASE failed in round {round}: {failure}");
                return CaseExit.Failed;
            }

            GC.Collect();
        }

        Console.WriteLine($"CASE walked onto a kill brick in {Rounds} levels, {Rounds * Asks * _refusals.Length} refused");
        return CaseExit.Passed;
    }

    // Null when the script was refused what it should be, was sent the
    // character for every touch, and the player was sent back each time.
    private static string? Play(out string wentOn)
    {
        Scene scene = new();
        Box(scene, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(8f, 0.5f, 8f)).BrushKind = BrushKind.Part;
        Box(scene, "Brick", new Vector3(2f, 0.05f, 0f), new Vector3(0.5f, 0.05f, 1f)).BrushKind = BrushKind.Part;
        Box(scene, "Wall", new Vector3(0f, 1f, -6f), new Vector3(4f, 1f, 0.25f));
        scene.Root.CreateChild("Killer").Script = new Script("Scripts/killer.luau");

        Heard sink = new();
        EntityWorld world = new(scene, NullLogger.Instance, new EntityCatalog());
        LuauScriptRuntime runtime = new(new ScriptRuntimeSetup(world, sink, new Files()));
        world.Scripts = runtime;

        var walker = new CharacterSimulation(scene) { SpawnPosition = new Vector3(0f, 0.05f, 0f) };
        var forward = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f) };
        try
        {
            world.Activate();
            walker.Spawn();
            world.Player = walker;
            for (int tick = 0; tick < Ticks; tick++)
            {
                world.Tick(world.FixedDeltaTime);
                runtime.BeforeSimulation();
                walker.Tick(in forward, world.FixedDeltaTime);
                runtime.AfterSimulation();
            }

            world.Deactivate();
        }
        finally
        {
            runtime.Close();
        }

        wentOn = sink.WentOn;
        return Judge(sink, walker);
    }

    private static string? Judge(Heard sink, CharacterSimulation walker)
    {
        if (sink.Errors > 0)
            return $"{sink.Errors} errors were reported, and every wrong call was caught by the script";
        if (sink.Refused.Count != Asks * _refusals.Length)
            return $"the script was refused {sink.Refused.Count} times";

        for (int i = 0; i < sink.Refused.Count; i++)
        {
            if (!sink.Refused[i].Contains(_refusals[i % _refusals.Length], StringComparison.Ordinal))
                return $"a wrong way to listen said '{sink.Refused[i]}'";
        }

        // "went on" with the touches, the ends and whether every touch was
        // handed the character.
        string[] parts = sink.WentOn.Split(' ');
        if (parts.Length != 5 || !int.TryParse(parts[2], out int kills) || !int.TryParse(parts[3], out int ends))
            return $"the script ended with '{sink.WentOn}'";
        if (kills < FewestKills || ends != kills || parts[4] != "true")
            return $"the script ended with '{sink.WentOn}'";

        return walker.State.Position.X < 2.5f ? null : "the player walked on past the brick";
    }

    private static SceneNode Box(Scene scene, string name, Vector3 center, Vector3 half)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = center;
        node.Brush = Brush.CreateBox(-half, half);
        return node;
    }

    private const string Killer = """
        local character = game:GetService("Players").LocalPlayer.Character
        local brick, wall = workspace.Brick, workspace.Wall
        local kills, ends, alwaysTheCharacter = 0, 0, true

        brick.Touched:Connect(function(hit)
            kills += 1
            alwaysTheCharacter = alwaysTheCharacter and hit == character
            hit:Respawn()
        end)
        brick.TouchEnded:Connect(function(hit)
            ends += 1
            alwaysTheCharacter = alwaysTheCharacter and hit == character
        end)

        local function refused(action)
            print('refused ' .. tostring(select(2, pcall(action))))
        end

        for ask = 1, ASKS do
            refused(function() wall.Touched:Connect(function() end) end)
            refused(function() wall.TouchEnded:Wait() end)
            refused(function() brick.Touched:Connect(ask) end)
            refused(function() brick.Touched = nil end)
        end

        local beats = 0
        game:GetService("RunService").Heartbeat:Connect(function()
            beats += 1
            if beats == LAST then
                print('went on ' .. kills .. ' ' .. ends .. ' ' .. tostring(alwaysTheCharacter))
            end
        end)
        """;

    private sealed class Files : IScriptSource
    {
        public byte[]? Read(string path, out string? problem)
        {
            Stress.Churn();
            problem = null;
            return Encoding.UTF8.GetBytes(Killer
                .Replace("ASKS", Asks.ToString(), StringComparison.Ordinal)
                .Replace("LAST", Ticks.ToString(), StringComparison.Ordinal));
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
