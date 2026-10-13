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

// A level that plays with a mover in Luau. Every tick it sweeps, slides and
// is refused a wrong write, raised out of a host function in a thread the
// host resumes. Then it goes wrong in one of a mover's ways, or the level is
// closed from another thread under its call. Every line runs the collector.
internal static class MoverCase
{
    private const int Ticks = 240;
    private const int FailsAt = 120;

    // How the mover goes wrong, and how the one line about it has to begin.
    // No line for the close: nobody is told of a level that stopped.
    private static readonly (string Way, string? Said)[] _ways =
    [
        ("error('no legs')", "The mover failed: no legs."),
        ("state.Velocity = Vector3.new(0 / 0, 0, 0)", "The mover failed: Velocity cannot be (NaN, 0, 0)"),
        ("collision:Sweep(state.Position, Vector3.new(1e30, 0, 0))", "The mover failed: A sweep cannot be"),
        ("task.wait()", "The mover waited"),
        ("coroutine.yield()", "The mover waited"),
        ("return state", "The mover returned a value"),
        ("while true do end", "The mover was stopped: the script ran for more than"),
        ("print('close')", null),
    ];

    internal static int Run()
    {
        string[] canaries = Stress.MakeCanaries();

        for (int round = 0; round < _ways.Length * 2; round++)
        {
            (string way, string? said) = _ways[round % _ways.Length];
            string? failure = Play(way, said) ?? Stress.FindDamage(canaries);
            if (failure is not null)
            {
                Console.WriteLine($"CASE failed in round {round + 1}, with a mover that does '{way}': {failure}");
                return CaseExit.Failed;
            }

            GC.Collect();
        }

        Console.WriteLine($"CASE moved the player by a mover in Luau in {_ways.Length * 2} levels, and the built-in mover took over in each");
        return CaseExit.Passed;
    }

    // Null when the mover moved the player until it went wrong, one line
    // said so or the level was closed, and the built-in mover walked the
    // player on.
    private static string? Play(string way, string? said)
    {
        Scene scene = new();
        Box(scene, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(30f, 0.5f, 8f)).BrushKind = BrushKind.Part;
        Box(scene, "Step", new Vector3(3f, 0.1f, 0f), new Vector3(0.5f, 0.1f, 2f)).BrushKind = BrushKind.Part;
        scene.Root.CreateChild("Mover").Script = new Script("Scripts/mover.luau");

        Heard sink = new();
        EntityWorld world = new(scene, NullLogger.Instance, new EntityCatalog());
        LuauScriptRuntime runtime = new(new ScriptRuntimeSetup(world, sink, new Files(way)))
        {
            CallLimit = TimeSpan.FromMilliseconds(100),
        };
        world.Scripts = runtime;
        sink.Runtime = runtime;

        var walker = new CharacterSimulation(scene) { SpawnPosition = new Vector3(0f, 0.05f, 0f) };
        var forward = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f) };
        float failedAt = 0f;
        bool wasClosed = false;
        try
        {
            world.Activate();
            walker.Spawn();
            world.Player = walker;
            for (int tick = 1; tick <= Ticks; tick++)
            {
                world.Tick(world.FixedDeltaTime);
                runtime.BeforeSimulation();
                walker.Tick(in forward, world.FixedDeltaTime);
                runtime.AfterSimulation();
                if (tick == FailsAt)
                {
                    failedAt = walker.State.Position.X;
                    wasClosed = runtime.IsClosed;
                }
            }

            world.Deactivate();
        }
        finally
        {
            runtime.Close();
        }

        string? told = said is null ? JudgeClose(sink, wasClosed) : JudgeLine(sink, said);
        return told ?? JudgeWalk(walker, failedAt);
    }

    private static string? JudgeLine(Heard sink, string said)
    {
        if (sink.Errors.Count != 1)
            return $"{sink.Errors.Count} errors were reported: {string.Join(" | ", sink.Errors)}";
        if (!sink.Errors[0].StartsWith(said, StringComparison.Ordinal) || !sink.Errors[0].EndsWith(LuauCharacterMover.TookOver, StringComparison.Ordinal))
            return $"the one error said '{sink.Errors[0]}'";

        return sink.Refused == FailsAt - 1
            ? null
            : $"the mover was refused a wrong write {sink.Refused} times before it went wrong";
    }

    // The call the close came in went on to its end, the state went with it,
    // and nobody was told anything.
    private static string? JudgeClose(Heard sink, bool wasClosed)
    {
        if (sink.Errors.Count != 0)
            return $"{sink.Errors.Count} errors were reported of a level that closed: {string.Join(" | ", sink.Errors)}";
        if (!sink.OutlivedTheClose)
            return "the state was gone before the mover's call had returned";
        if (!wasClosed)
            return "the state was still there after the mover's call had returned";

        return sink.Refused == FailsAt
            ? null
            : $"the mover was refused a wrong write {sink.Refused} times, and it ran {FailsAt} times";
    }

    private static string? JudgeWalk(CharacterSimulation walker, float failedAt)
    {
        if (!ReferenceEquals(walker.Mover, DefaultCharacterMover.Instance))
            return "the built-in mover is not back after the level stopped";

        CharacterState state = walker.State;
        if (!float.IsFinite(state.Position.X) || !state.Grounded)
            return $"the player ended at {state.Position}, standing: {state.Grounded}";

        return failedAt > 2f && state.Position.X > failedAt + 2f
            ? null
            : $"the player was at {failedAt} when the mover went wrong and at {state.Position.X} in the end";
    }

    private static SceneNode Box(Scene scene, string name, Vector3 center, Vector3 half)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = center;
        node.Brush = Brush.CreateBox(-half, half);
        return node;
    }

    // A walk with no step up and no jump: fall, sweep and slide, and stand
    // on what is under the feet.
    private const string Mover = """
        local character = game:GetService("Players").LocalPlayer.Character
        local ticks = 0
        character:SetMover(function(state, command, collision, tuning, dt)
            ticks += 1
            if ticks == FAILS_AT then
                WAY
            end

            print('refused ' .. tostring(select(2, pcall(function() state.Position = 'here' end))))

            local fall = if state.IsGrounded then 0 else state.Velocity.Y + tuning.Gravity.Y * dt
            local velocity = command.MoveDirection * tuning.WalkSpeed + Vector3.new(0, fall, 0)
            local position = state.Position
            local left = velocity * dt
            collision:ClearPlanes()
            for _ = 1, 3 do
                local went, normal, gap, _, part = collision:Sweep(position, left)
                position += left * went
                if went >= 1 then break end
                if part and gap <= tuning.SkinWidth * 2 then state:AddSlideContact(part) end
                collision:AddPlane(normal)
                left = collision:Clip(left * (1 - went))
                velocity = collision:Clip(velocity)
            end

            if collision:Gather(position, 0) > 0 then
                position += collision:Solve(Vector3.zero)
            end

            local stands = false
            for index = 1, collision:Gather(position, 0.05) do
                local normal, _, part = collision:Plane(index)
                if normal.Y > 0.7 then
                    state:StandOn(normal, part)
                    stands = true
                end
            end
            if not stands then state:LeaveGround() end

            state:SetMemory(1, state:GetMemory(1) + 1)
            state.Position = position
            state.Velocity = velocity
        end)
        """;

    private sealed class Files(string way) : IScriptSource
    {
        public byte[]? Read(string path, out string? problem)
        {
            Stress.Churn();
            problem = null;
            return Encoding.UTF8.GetBytes(Mover
                .Replace("FAILS_AT", FailsAt.ToString(), StringComparison.Ordinal)
                .Replace("WAY", way, StringComparison.Ordinal));
        }
    }

    // Keeps what the host reports, and runs the collector on every line.
    // A line that says close has the level's runtime closed by another
    // thread, as the editor does after its engine thread died.
    private sealed class Heard : IScriptSink
    {
        public List<string> Errors { get; } = [];

        public int Refused { get; private set; }

        public LuauScriptRuntime? Runtime { get; set; }

        // Whether the state was still there when the close came back.
        public bool OutlivedTheClose { get; private set; }

        public void Report(in ScriptMessage message)
        {
            Stress.Churn();
            if (message.Severity == ScriptSeverity.Error)
                Errors.Add(message.Text);
            else if (message.Text.StartsWith("refused ", StringComparison.Ordinal))
                Refused++;
            else if (message.Text == "close" && Runtime is { } runtime)
                OutlivedTheClose = CloseFromAnotherThread(runtime);
        }

        private static bool CloseFromAnotherThread(LuauScriptRuntime runtime)
        {
            Thread closer = new(runtime.Close);
            closer.Start();
            closer.Join();
            return !runtime.IsClosed;
        }
    }
}
