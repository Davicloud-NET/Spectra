using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The tags and attributes scripts set on nodes that are not destroyed count
/// against the memory budget by their bytes. What a node was built with is
/// the level's and counts for nothing.
/// </summary>
public sealed class NodeBytesTests
{
    private const long Megabyte = 1024 * 1024;

    [Fact]
    public void A_script_that_sets_long_attributes_on_parts_of_the_level_past_the_budget_is_switched_off_and_told_why()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            task.wait()
            local blob = string.rep("x", 65536)
            while true do
                local part = Instance.new("Part")
                part.Parent = workspace
                part:SetAttribute("Blob", blob)
            end
            """, "Hoarder");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();
        runtime.MemoryBudget = 8 * Megabyte;

        level.Tick(2);

        ScriptMessage error = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        error.Text.ShouldBe(
            "the scripts hold more than 8 MB, with the tags and attributes they set on nodes, and this one was switched off");
        error.Script.ShouldBe("Scripts/Hoarder1.luau");
        error.Line.ShouldBe(6);
        runtime.NodeBytes.ShouldBeGreaterThan(7 * Megabyte);

        // The attribute that went over was not kept.
        runtime.Collect();
        (runtime.NodeBytes + runtime.LuauBytes).ShouldBeLessThanOrEqualTo(8 * Megabyte);
    }

    [Fact]
    public void A_script_that_catches_the_stop_is_stopped_all_the_same()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            task.wait()
            local blob = string.rep("x", 65536)
            while true do
                pcall(function()
                    local part = Instance.new("Part")
                    part.Parent = workspace
                    part:SetAttribute("Blob", blob)
                end)
            end
            """, "Hoarder");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();
        runtime.MemoryBudget = 8 * Megabyte;

        level.Tick(2);

        level.Errors.ShouldHaveSingleItem().ShouldStartWith("the scripts hold more than 8 MB");
    }

    // The value that went over is not kept, so the rest of the level is
    // still under the budget and its scripts are not stopped for it.
    [Fact]
    public void The_other_scripts_of_the_level_run_on()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            task.wait()
            local blob = string.rep("x", 65536)
            while true do
                local part = Instance.new("Part")
                part.Parent = workspace
                part:SetAttribute("Blob", blob)
            end
            """, "Hoarder");
        level.AddScript(level.Scene.Root, """
            local ticks = 0
            game:GetService("RunService").Heartbeat:Connect(function()
                ticks += 1
                local scrap = table.create(100, ticks)
                if ticks % 5 == 0 then
                    print(ticks)
                end
            end)
            """, "Counter");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();
        runtime.MemoryBudget = 8 * Megabyte;

        level.Tick(10);

        level.Errors.ShouldHaveSingleItem();
        level.Printed.ShouldBe(["5", "10"]);
    }

    [Fact]
    public void A_script_that_gives_parts_tags_past_the_budget_is_switched_off()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            task.wait()
            local stem = string.rep("t", 200)
            while true do
                local part = Instance.new("Part")
                part.Parent = workspace
                for index = 1, 256 do
                    part:AddTag(stem .. index)
                end
            end
            """, "Tagger");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();
        runtime.MemoryBudget = 4 * Megabyte;

        level.Tick(2);

        level.Errors.ShouldHaveSingleItem().ShouldBe(
            "the scripts hold more than 4 MB, with the tags and attributes they set on nodes, and this one was switched off");
        runtime.Collect();
        (runtime.NodeBytes + runtime.LuauBytes).ShouldBeLessThanOrEqualTo(4 * Megabyte);
    }

    [Fact]
    public void A_clone_that_goes_past_the_budget_is_not_made()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            task.wait()
            local model = Instance.new("Folder")
            for index = 1, 40 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", string.rep("x", 65536))
                part.Parent = model
            end
            model.Parent = workspace
            local copies = {}
            while true do
                copies[#copies + 1] = model:Clone()
            end
            """, "Cloner");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();
        runtime.MemoryBudget = 8 * Megabyte;

        level.Tick(2);

        level.Errors.ShouldHaveSingleItem().ShouldStartWith("the scripts hold more than 8 MB, with the tags");
        runtime.Collect();
        (runtime.NodeBytes + runtime.LuauBytes).ShouldBeLessThanOrEqualTo(8 * Megabyte);
        runtime.NodeBytes.ShouldBe(runtime.CountNodeBytes());
    }

    // The destroyed parts weigh more than the model, and less than the model
    // with its copy. The message is made while the copy still counts.
    [Fact]
    public void A_clone_past_the_budget_is_told_of_as_what_scripts_set_on_nodes()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            task.wait()
            local blob = string.rep("x", 65536)
            local held = {}
            for index = 1, 55 do
                local part = Instance.new("Part")
                part.Parent = workspace
                part:SetAttribute("Blob", blob)
                part:Destroy()
                held[index] = part
            end
            local model = Instance.new("Folder")
            for index = 1, 45 do
                local part = Instance.new("Part")
                part:SetAttribute("Blob", blob)
                part.Parent = model
            end
            model.Parent = workspace
            print("built")
            local copy = model:Clone()
            print("cloned", #held, copy)
            """, "Cloner");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();
        runtime.MemoryBudget = 8 * Megabyte;

        level.Tick(2);

        level.Printed.ShouldBe(["built"]);
        level.Errors.ShouldHaveSingleItem().ShouldBe(
            "the scripts hold more than 8 MB, with the tags and attributes they set on nodes, and this one was switched off");
    }

    [Fact]
    public void The_count_goes_down_when_an_attribute_or_a_tag_is_taken_away_and_when_the_node_is_destroyed()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local part = Instance.new("Part")
            part.Parent = workspace
            part:SetAttribute("Blob", string.rep("x", 1000))
            part:AddTag("Hot")
            task.wait()
            part:SetAttribute("Blob", nil)
            task.wait()
            part:RemoveTag("Hot")
            part:AddTag("Cold")
            task.wait()
            part:Destroy()
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        long blob = NodeWeight.OfAttribute + NodeWeight.OfText + (2 * "Blob".Length) + NodeWeight.OfBytes + 1000;
        runtime.NodeBytes.ShouldBe(blob + NodeWeight.OfTag + (2 * "Hot".Length));
        level.Tick();
        runtime.NodeBytes.ShouldBe(NodeWeight.OfTag + (2 * "Hot".Length));
        level.Tick();
        runtime.NodeBytes.ShouldBe(NodeWeight.OfTag + (2 * "Cold".Length));
        level.Tick();

        level.Errors.ShouldBeEmpty();
        runtime.NodeBytes.ShouldBe(0);
    }

    [Fact]
    public void Nothing_is_counted_after_Stop_and_the_next_play_counts_the_same()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            for index = 1, 10 do
                local part = Instance.new("Part")
                part.Parent = workspace
                part:SetAttribute("Blob", string.rep("x", 4096))
                part:AddTag("Kept")
            end
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        long counted = runtime.NodeBytes;
        counted.ShouldBeGreaterThan(10 * 4096);

        level.Stop();
        runtime.NodeBytes.ShouldBe(0);
        LuauScriptRuntime next = level.Play().Tick().Runtime.ShouldNotBeNull();

        next.ShouldNotBeSameAs(runtime);
        next.NodeBytes.ShouldBe(counted);
    }

    [Fact]
    public void What_a_node_was_built_with_counts_for_nothing_also_when_a_script_sets_it_again()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        SceneNode sign = level.Part("Sign", new Vector3(0f, 2f, 0f));
        sign.Attributes = Built(("Speed", MemberValue.Of(3d)), ("Note", MemberValue.Of(new string('n', 1000))));
        sign.Tags = TagSet.Of("Built");
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            task.wait()
            sign:SetAttribute("Speed", 5)
            task.wait()
            sign:SetAttribute("Speed", 3)
            sign:SetAttribute("Note", nil)
            sign:RemoveTag("Built")
            sign:AddTag("Built")
            """, "Main");

        LuauScriptRuntime runtime = level.Play().Tick().Runtime.ShouldNotBeNull();
        runtime.NodeBytes.ShouldBe(0);
        level.Tick();
        runtime.NodeBytes.ShouldBe(NodeWeight.OfAttribute + NodeWeight.OfText + (2 * "Speed".Length));
        level.Tick();

        level.Errors.ShouldBeEmpty();
        runtime.NodeBytes.ShouldBe(0);
        runtime.CountNodeBytes().ShouldBe(0);
    }

    // Built nodes and made ones, in the level and loose, cloned and
    // destroyed, against the count made again from every node.
    [Fact]
    public void A_seeded_random_run_never_drifts_from_a_recount()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        for (int index = 1; index <= 4; index++)
        {
            SceneNode built = level.Part($"Built{index}", new Vector3(index, 2f, 0f));
            built.Attributes = Built(("A", MemberValue.Of(index)), ("Long", MemberValue.Of(new string('b', 300))));
            built.Tags = TagSet.Of("Red");
        }

        level.AddScript(level.Scene.Root, """
            math.randomseed(20261014)
            local names = { "A", "B", "C", "Long", "Other" }
            local tags = { "Red", "Green", "Blue", "Purple" }
            local pool = {}
            for index = 1, 4 do
                pool[index] = workspace["Built" .. index]
            end
            for index = 5, 24 do
                local part = Instance.new("Part")
                part.Parent = workspace
                pool[index] = part
            end

            local function fresh()
                local part = Instance.new("Part")
                part.Parent = if math.random(2) == 1 then workspace else nil
                return part
            end

            game:GetService("RunService").Heartbeat:Connect(function()
                for step = 1, 40 do
                    local slot = math.random(#pool)
                    local part = pool[slot]
                    local roll = math.random(14)
                    if roll <= 3 then
                        part:SetAttribute(names[math.random(#names)], math.random(4))
                    elseif roll <= 5 then
                        part:SetAttribute(names[math.random(#names)], string.rep("x", math.random(1, 300)))
                    elseif roll <= 7 then
                        part:SetAttribute(names[math.random(#names)], nil)
                    elseif roll == 8 then
                        part:AddTag(tags[math.random(#tags)])
                    elseif roll == 9 then
                        part:RemoveTag(tags[math.random(#tags)])
                    elseif roll == 10 then
                        part.Parent = if part.Parent then nil else workspace
                    elseif roll == 11 then
                        local ok, copy = pcall(part.Clone, part)
                        if ok and copy then
                            copy.Parent = if math.random(2) == 1 then workspace else nil
                            pool[math.random(#pool)] = copy
                        end
                    elseif roll == 12 then
                        part:Destroy()
                        pool[slot] = fresh()
                    else
                        pool[slot] = fresh()
                    end
                end
            end)
            """, "Main");
        LuauScriptRuntime runtime = level.Play().Runtime.ShouldNotBeNull();

        for (int tick = 0; tick < 300; tick++)
        {
            level.Tick();
            runtime.NodeBytes.ShouldBe(runtime.CountNodeBytes(), $"after tick {tick}");
        }

        level.Errors.ShouldBeEmpty();
        runtime.NodeBytes.ShouldBeGreaterThan(0);
        level.Stop();
        runtime.NodeBytes.ShouldBe(0);
    }

    private static AttributeSet Built(params (string Name, MemberValue Value)[] pairs)
    {
        var list = pairs.Select(pair => new KeyValuePair<string, MemberValue>(pair.Name, pair.Value)).ToList();
        AttributeSet.TryMake(list, out AttributeSet? made, out string? refusal).ShouldBeTrue(refusal);
        return made;
    }
}
