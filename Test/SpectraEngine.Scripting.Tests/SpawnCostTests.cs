using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What it costs a level to spawn and to destroy. A node is new memory, so
/// a spawn allocates. What must hold is that nothing is kept for a node
/// that is gone, however long a level spawns. The times are a measurement
/// that runs when asked for, and are then written beside the test binary,
/// in spawn-costs.txt.
/// </summary>
public sealed class SpawnCostTests
{
    private const int WarmTicks = 300;
    private const int Ticks = 1200;

    // What a level does on every tick, by what it is called in the table.
    // Each takes the template and gives back what it made, or nothing.
    private static readonly (string Name, string Spawn)[] Spawns =
    [
        ("make a Folder, parent it, destroy it", "local made = Instance.new('Folder') made.Parent = workspace made:Destroy()"),
        ("make a Part, parent it, destroy it", "local made = Instance.new('Part') made.Parent = workspace made:Destroy()"),
        ("make a Part with its parent, destroy it", "Instance.new('Part', workspace):Destroy()"),
        ("clone a part, parent it, destroy it", "local made = part:Clone() made.Parent = workspace made:Destroy()"),
        ("clone a part and let go of it", "local _ = part:Clone()"),
        ("clone a part with a light and a tag", "local made = lit:Clone() made.Parent = workspace made:Destroy()"),
        ("clone a part with a script, a tick in the level", "local made = scripted:Clone() made.Parent = workspace task.delay(0, made.Destroy, made)"),
        ("clone a door, a tick in the level", "local made = door:Clone() made.Parent = workspace task.delay(0, made.Destroy, made)"),
        ("clone a group of ten parts, parent it, destroy it", "local made = group:Clone() made.Parent = workspace made:Destroy()"),
    ];

    // Spawns a part with a listener, a tag and an attribute on every tick,
    // and destroys the one it made ten ticks before.
    private const string Churn = """
        local template = workspace.Template
        local alive = {}
        game:GetService("RunService").Heartbeat:Connect(function()
            local part = template:Clone()
            part.Position = Vector3.new(#alive, 5, 0)
            part.Touched:Connect(function() end)
            part:AddTag("Spawned")
            part:SetAttribute("Age", 0)
            part.Parent = workspace
            table.insert(alive, part)
            if #alive > 10 then
                table.remove(alive, 1):Destroy()
            end
        end)
        """;

    [Fact]
    public void A_level_that_spawns_and_destroys_a_part_on_every_tick_keeps_nothing_for_the_parts_that_are_gone()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.Part("Template", new Vector3(0f, 5f, 30f));
        level.AddScript(level.Scene.Root, Churn, "Churn");
        int built = level.Scene.NodeCount;

        level.Play().Tick(WarmTicks);
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.Collect();
        int held = runtime.HeldObjectCount;
        int leaves = level.Scene.Bvh.LeafCount;
        WeakReference<SceneNode>[] spawned = SpawnedNow(level);

        level.Tick(Ticks);
        runtime.Collect();

        level.Errors.ShouldBeEmpty();
        TreeWrites tree = new ScriptWrites(level.Entities).Tree;
        tree.MadeCount.ShouldBe(10);
        level.Scene.NodeCount.ShouldBe(built + 10);
        level.Scene.Bvh.LeafCount.ShouldBe(leaves);
        level.Scene.DrawableBvh.LeafCount.ShouldBe(leaves - 1, "the ground is drawn with the static world");
        runtime.TaggedNodeCount.ShouldBe(10);
        runtime.AttributeNodeCount.ShouldBe(10);
        runtime.NodeSignalCount.ShouldBe(10);
        runtime.LooseTreeCount.ShouldBe(0);
        runtime.HeldObjectCount.ShouldBeLessThanOrEqualTo(held + 4);
        level.Entities.Listeners.Count.ShouldBe(10);
        level.Entities.Journal.Count.ShouldBe(0);
        level.Entities.Journal.Tree.TouchedParentCount.ShouldBe(1);

        // The ten that were in the level then are long destroyed. Nothing
        // in the engine or the runtime holds one of them.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        spawned.Length.ShouldBe(10);
        spawned.Count(node => node.TryGetTarget(out _)).ShouldBe(0, "a destroyed part is still held");
    }

    // In a method of its own, so no local keeps a node alive in the test.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<SceneNode>[] SpawnedNow(ScriptLevel level) =>
        [.. level.Scene.Root.Children.Where(TreeWrites.IsMade).Select(node => new WeakReference<SceneNode>(node))];

    [Fact]
    public void A_spawn_and_a_destroy_cost_the_static_world_nothing()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.Part("Template", new Vector3(0f, 5f, 30f));
        level.AddScript(level.Scene.Root, Churn, "Churn");
        int compiles = level.Scene.StaticWorldCompileCount;

        level.Play().Tick(WarmTicks);
        level.Stop();

        level.Errors.ShouldBeEmpty();
        level.Scene.StaticWorldDirty.ShouldBeFalse();
        level.Scene.StaticWorldCompileCount.ShouldBe(compiles);
    }

    // A measurement and no test: it has nothing to fail on, so it runs only
    // when asked for, with -explicit only.
    [Fact(Explicit = true)]
    public void The_cost_of_every_spawn_is_written_down()
    {
        var table = new StringBuilder();
        foreach ((string name, string spawn) in Spawns)
        {
            (double microseconds, double bytes) = Measure(spawn);
            table.AppendLine(string.Create(
                CultureInfo.InvariantCulture, $"{name,-52} {microseconds,7:0.00} us  {bytes,7:0} bytes"));
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "spawn-costs.txt"), table.ToString());
    }

    // The time and the bytes of one tick that does the spawn once, with what
    // the tick itself costs taken off.
    private static (double Microseconds, double Bytes) Measure(string spawn)
    {
        (double Microseconds, double Bytes) idle = Run("");
        (double Microseconds, double Bytes) busy = Run(spawn);
        return (Math.Max(0, busy.Microseconds - idle.Microseconds), Math.Max(0, busy.Bytes - idle.Bytes));
    }

    private static (double Microseconds, double Bytes) Run(string spawn)
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.Part("Part", new Vector3(0f, 5f, 30f));
        SceneNode lit = level.Part("Lit", new Vector3(3f, 5f, 30f));
        lit.CreateChild("Glow").Light = new Light { Kind = LightKind.Point };
        level.AddScript(level.Part("Scripted", new Vector3(6f, 5f, 30f)), "script.Parent:SetAttribute('Ran', true)", "Runs");
        SceneNode door = level.Part("Door", new Vector3(9f, 5f, 30f));
        door.Entity = new EntityData("func_door");
        SceneNode group = level.Scene.Root.CreateChild("Group");
        for (int i = 0; i < 10; i++)
            level.Part($"Brick{i}", new Vector3(i, 8f, 30f), group);

        level.AddScript(level.Scene.Root, $$"""
            local part, lit, scripted, door, group = workspace.Part, workspace.Lit, workspace.Scripted, workspace.Door, workspace.Group
            lit:AddTag("Bright")
            game:GetService("RunService").Heartbeat:Connect(function()
                {{spawn}}
            end)
            """, "Spawner");

        level.Play().Tick(WarmTicks);

        double microseconds = double.MaxValue;
        long allocated = long.MaxValue;
        for (int run = 0; run < 3; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            level.Tick(Ticks);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
            microseconds = Math.Min(microseconds, elapsed.TotalMicroseconds / Ticks);
        }

        level.Errors.ShouldBeEmpty();
        return (microseconds, allocated / (double)Ticks);
    }
}
