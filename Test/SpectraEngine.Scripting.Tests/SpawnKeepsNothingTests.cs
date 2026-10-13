using System.Numerics;
using System.Runtime.CompilerServices;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A level that clones a template on every tick and destroys the clone of
/// ten ticks before keeps nothing for the clones that are gone, whatever
/// the template carries: a script, a module its script asks for, an entity.
/// </summary>
public sealed class SpawnKeepsNothingTests
{
    private const int WarmTicks = 120;
    private const int Ticks = 600;
    private const int Alive = 10;

    private const string Churn = """
        local template = workspace.Template
        local alive = {}
        game:GetService("RunService").Heartbeat:Connect(function()
            local copy = template:Clone()
            copy.Name = "Spawned"
            copy.Parent = workspace
            table.insert(alive, copy)
            if #alive > 10 then
                table.remove(alive, 1):Destroy()
            end
        end)
        """;

    [Fact]
    public void A_clone_with_a_script_that_listens_and_waits_is_let_go_of_when_it_is_destroyed()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        SceneNode template = level.Part("Template", new Vector3(0f, 5f, 30f));
        level.AddScript(template, """
            game:GetService("RunService").Heartbeat:Connect(function() end)
            script.Parent.Touched:Connect(function() end)
            while true do
                task.wait(1)
            end
            """, "Busy");

        Churned(level, nodesInAClone: 2);

        // The template's, the level's and those of the clones. The newest
        // clone's starts at the end of the next tick.
        level.Runtime.ShouldNotBeNull().RunningScriptCount.ShouldBe(Alive + 1);
    }

    [Fact]
    public void A_clone_with_a_module_its_script_asks_for_is_let_go_of_when_it_is_destroyed()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        SceneNode template = level.Part("Template", new Vector3(0f, 5f, 30f));
        SceneNode config = level.AddScript(template, """
            task.spawn(function() end)
            return { speed = 4, owner = script }
            """, "Config");
        config.Script = config.Script.ShouldNotBeNull() with { IsModule = true };
        level.AddScript(template, """
            local config = require(script.Parent.Config)
            script.Parent:SetAttribute("Speed", config.speed)
            """, "Reads");

        Churned(level, nodesInAClone: 3);
        level.Printed.ShouldBeEmpty();
    }

    [Fact]
    public void A_clone_with_a_door_and_a_wire_is_let_go_of_when_it_is_destroyed()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        var door = new EntityData("func_door");
        door.SetValue("speed", "6");
        door.Connections.Add(new EntityConnection("OnFullyOpen", "!self", "Close", "", 0f, EntityConnection.Infinite));
        level.Part("Template", new Vector3(0f, 5f, 30f)).Entity = door;

        Churned(level, nodesInAClone: 1);
        level.Entities.Entities.Count.ShouldBe(Alive + 1, "the clones' doors and the template's");
    }

    // A destroyed node keeps its tags and attributes for the script that
    // holds it, as in Roblox. They go when the node does.
    [Fact]
    public void A_clone_whose_tags_and_attributes_are_read_and_set_after_it_was_destroyed_is_let_go_of()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        SceneNode template = level.Part("Template", new Vector3(0f, 5f, 30f));
        template.Tags = TagSet.Of("Lava");
        template.Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(25d));

        Churned(level, nodesInAClone: 1, """
            local template = workspace.Template
            local alive = {}
            game:GetService("RunService").Heartbeat:Connect(function()
                local copy = template:Clone()
                copy.Name = "Spawned"
                copy:SetAttribute("Age", 0)
                copy.Parent = workspace
                table.insert(alive, copy)
                if #alive > 10 then
                    local old = table.remove(alive, 1)
                    old:Destroy()
                    old:AddTag("Gone")
                    old:SetAttribute("Age", 10)
                    if not (old:HasTag("Lava") and old:HasTag("Gone")
                        and old:GetAttribute("Damage") == 25 and old:GetAttribute("Age") == 10) then
                        print("a destroyed clone lost what it held")
                    end
                end
            end)
            """);

        level.Printed.ShouldBeEmpty();
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.TaggedNodeCount.ShouldBe(Alive + 1, "the clones in the level and the template");
        runtime.AttributeNodeCount.ShouldBe(Alive + 1);
    }

    // Plays the level with the churn on it and holds it to keeping nothing.
    private static void Churned(ScriptLevel level, int nodesInAClone, string churn = Churn)
    {
        level.AddScript(level.Scene.Root, churn, "Churn");
        int built = level.Scene.NodeCount;

        level.Play().Tick(WarmTicks);
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.Collect();
        int held = runtime.HeldObjectCount;
        int waiting = runtime.WaitingThreadCount;
        WeakReference<SceneNode>[] spawned = SpawnedNow(level);

        level.Tick(Ticks);
        runtime.Collect();

        level.Errors.ShouldBeEmpty();
        spawned.Length.ShouldBe(Alive * nodesInAClone);
        level.Scene.NodeCount.ShouldBe(built + Alive * nodesInAClone);
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(Alive * nodesInAClone);
        runtime.LooseTreeCount.ShouldBe(0);
        runtime.HeldDestroyedCount.ShouldBe(0);
        runtime.WaitingThreadCount.ShouldBe(waiting);
        runtime.HeldObjectCount.ShouldBeLessThanOrEqualTo(held + 4);
        level.Entities.Journal.Count.ShouldBe(0);

        // The clones that were in the level then are long destroyed.
        // Nothing in the engine or the runtime holds a node of one.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        spawned.Count(node => node.TryGetTarget(out _)).ShouldBe(0, "a node of a destroyed clone is still held");
    }

    // In a method of its own, so no local keeps a node alive in the test.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<SceneNode>[] SpawnedNow(ScriptLevel level) =>
        [.. level.Scene.Root.Traverse().Where(TreeWrites.IsMade).Select(node => new WeakReference<SceneNode>(node))];
}
