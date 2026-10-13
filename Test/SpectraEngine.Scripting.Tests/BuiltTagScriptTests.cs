using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The tags and attributes a level was built with, as its scripts find them:
/// there from the first line of the first script, changed by scripts in a
/// copy the play session owns, and on the node as they were once it stops.
/// </summary>
public sealed class BuiltTagScriptTests
{
    private const string Service = "local CollectionService = game:GetService(\"CollectionService\")\n";

    private const string Names = """
        local function names(nodes)
            local list = {}
            for index, node in nodes do
                list[index] = node.Name
            end
            return #list .. ": " .. table.concat(list, " ")
        end

        """;

    [Fact]
    public void The_first_line_of_the_first_script_finds_what_the_level_was_built_with()
    {
        using var level = new ScriptLevel();
        SceneNode first = level.AddScript(level.Scene.Root, Service + Names + """
            print(names(CollectionService:GetTagged("Lava")))
            local brick = workspace.Brick
            print(brick:HasTag("Lava"), brick:HasTag("lava"), table.concat(brick:GetTags(), " | "))
            print(brick:GetAttribute("Damage"), brick:GetAttribute("Missing"))
            """);
        level.Scene.Root.InsertChild(0, first);
        Bricks(level);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["3: Pool Brick Ember", "true false Lava | Hot stuff", "25 nil"]);
    }

    [Fact]
    public void A_tags_nodes_come_in_the_order_of_the_tree_whatever_order_they_were_tagged_in()
    {
        using var level = new ScriptLevel();
        Bricks(level);
        level.Baseplate.Tags = TagSet.Of("Lava");
        level.AddScript(level.Scene.Root, Service + Names + """
            print(names(CollectionService:GetTagged("Lava")))
            print(names(CollectionService:GetTagged("Hot stuff")), names(CollectionService:GetTagged("Nobody")))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["4: Baseplate Pool Brick Ember", "1: Brick 0: "]);
    }

    [Fact]
    public void An_attribute_of_every_kind_reads_as_the_value_it_was_built_with()
    {
        using var level = new ScriptLevel();
        Bricks(level);
        level.AddScript(level.Scene.Root, """
            local brick = workspace.Brick
            for _, name in { "Damage", "Label", "Deadly", "Push", "Glow", "Rest" } do
                local value = brick:GetAttribute(name)
                print(name, typeof(value), value)
            end
            local all = {}
            for name in brick:GetAttributes() do
                table.insert(all, name)
            end
            table.sort(all)
            print(table.concat(all, " "))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "Damage number 25",
            "Label string hot",
            "Deadly boolean true",
            "Push Vector3 0, 12.5, -1",
            "Glow Color3 1, 0.5, 0",
            "Rest CFrame 1, 2, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1",
            "Damage Deadly Glow Label Push Rest",
        ]);
    }

    [Fact]
    public void No_signal_fires_for_what_the_level_was_built_with()
    {
        using var level = new ScriptLevel();
        Bricks(level);
        level.AddScript(level.Scene.Root, Service + """
            local added, changed = 0, 0
            CollectionService:GetInstanceAddedSignal("Lava"):Connect(function() added += 1 end)
            workspace.Brick.AttributeChanged:Connect(function() changed += 1 end)
            task.wait()
            task.wait()
            print(added, changed)
            workspace.Baseplate:AddTag("Lava")
            workspace.Brick:SetAttribute("Damage", 26)
            task.wait()
            print(added, changed)
            """);

        level.Play().Tick(6);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["0 0", "1 1"]);
    }

    [Fact]
    public void What_a_script_changes_is_the_play_sessions_copy_and_never_the_node()
    {
        using var level = new ScriptLevel();
        (SceneNode brick, _, _) = Bricks(level);
        TagSet tags = brick.Tags;
        AttributeSet attributes = brick.Attributes;
        level.AddScript(level.Scene.Root, Service + Names + """
            local brick = workspace.Brick
            brick:RemoveTag("Lava")
            brick:AddTag("Cooled")
            brick:SetAttribute("Damage", 99)
            brick:SetAttribute("Label", nil)
            brick:SetAttribute("Fresh", true)
            print(names(CollectionService:GetTagged("Lava")), table.concat(brick:GetTags(), " | "))
            print(brick:GetAttribute("Damage"), brick:GetAttribute("Label"), brick:GetAttribute("Fresh"))
            """);
        var before = new LevelState(level.Scene);

        string[] expected = ["2: Pool Ember Hot stuff | Cooled", "99 nil true"];
        for (int play = 1; play <= 2; play++)
        {
            level.Play().Tick();

            // While it plays and after it stopped, the node has what it was built with.
            level.Errors.ShouldBeEmpty();
            level.Printed.ShouldBe([.. Enumerable.Repeat(expected, play).SelectMany(lines => lines)]);
            brick.Tags.ShouldBeSameAs(tags);
            brick.Attributes.ShouldBeSameAs(attributes);

            level.Stop();
            brick.Tags.ShouldBeSameAs(tags);
            brick.Attributes.ShouldBeSameAs(attributes);
            new LevelState(level.Scene).ShouldBe(before, $"play {play}");
            level.Runtime.ShouldNotBeNull().TaggedNodeCount.ShouldBe(0);
            level.Runtime.AttributeNodeCount.ShouldBe(0);
        }
    }

    [Fact]
    public void On_an_entity_a_keyvalue_wins_over_an_attribute_of_its_name_and_the_level_says_so_once()
    {
        using var level = new ScriptLevel();
        SceneNode sign = level.Scene.Root.CreateChild("Sign");
        sign.Entity = new EntityData(SignEntity.Class);
        sign.Entity.SetValue("note", "only the node has it");
        sign.Attributes = AttributeSet.Empty
            .With("speed", MemberValue.Of(9d))
            .With("note", MemberValue.Of("an attribute"))
            .With("Score", MemberValue.Of(7d));
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            print(sign:GetAttribute("speed"), sign:GetAttribute("note"), sign:GetAttribute("Score"))
            print(sign:GetAttributes().speed, sign:GetAttributes().Score)
            """);

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["1.5 only the node has it 7", "1.5 7"]);
        level.Sink.Texts(ScriptSeverity.Warning).ShouldBe(
        [
            "'Sign' has an attribute called 'speed', and its entity has a setting of that name. " +
            "Scripts read the entity's setting. Give the attribute another name.",
            "'Sign' has an attribute called 'note', and its entity has a setting of that name. " +
            "Scripts read the entity's setting. Give the attribute another name.",
        ]);
    }

    [Fact]
    public void A_node_that_comes_into_the_level_later_brings_what_it_was_built_with_and_is_read_once()
    {
        using var level = new ScriptLevel();
        Bricks(level);
        var late = new SceneNode("Late")
        {
            Tags = TagSet.Of("Lava"),
            Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(5d)),
        };
        level.AddScript(level.Scene.Root, Service + Names + """
            CollectionService:GetInstanceAddedSignal("Lava"):Connect(function(node)
                print("added", node.Name, node:GetAttribute("Damage"))
                node:RemoveTag("Lava")
                node:SetAttribute("Damage", 6)
            end)
            task.wait(0.2)
            print(names(CollectionService:GetTagged("Lava")), workspace.Late:GetAttribute("Damage"))
            """);

        level.Play().Tick();
        level.Scene.Root.AddChild(late);
        level.Tick(2);

        // Out and in again: the copy the script changed is the one that counts.
        level.Scene.Root.RemoveChild(late);
        level.Scene.Root.AddChild(late);
        level.Tick(20);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["added Late 5", "3: Pool Brick Ember 6"]);
        late.Tags.ShouldBe(["Lava"]);
    }

    [Fact]
    public void A_node_built_full_of_tags_and_attributes_takes_no_more_from_a_script()
    {
        using var level = new ScriptLevel();
        SceneNode full = level.Part("Full", new Vector3(0f, 2f, 0f));
        full.Tags = TagSet.Of([.. Enumerable.Range(0, TagLimits.MaxPerNode).Select(i => $"Tag{i}")]);
        AttributeSet.TryMake(
            [.. Enumerable.Range(0, AttributeLimits.MaxPerNode)
                .Select(i => new KeyValuePair<string, MemberValue>($"Name{i}", MemberValue.Of((double)i)))],
            out AttributeSet? attributes, out _).ShouldBeTrue();
        full.Attributes = attributes.ShouldNotBeNull();
        level.AddScript(level.Scene.Root, """
            local full = workspace.Full
            print(#full:GetTags(), full:GetAttribute("Name255"))
            print(pcall(full.AddTag, full, "OneMore"))
            print(pcall(full.SetAttribute, full, "OneMore", 1))
            full:AddTag("Tag7")
            full:SetAttribute("Name7", 700)
            print(full:GetAttribute("Name7"))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.Length.ShouldBe(4);
        level.Printed[0].ShouldBe("256 255");
        level.Printed[1].ShouldContain("has 256 tags, and a node has no more");
        level.Printed[2].ShouldContain("has 256 attributes, and a node holds no more");
        level.Printed[3].ShouldBe("700");
    }

    // Three parts with the tag Lava, tagged in an order that is not the
    // tree's. Brick has a second tag and an attribute of every kind.
    private static (SceneNode Brick, SceneNode Pool, SceneNode Ember) Bricks(ScriptLevel level)
    {
        SceneNode pool = level.Part("Pool", new Vector3(4f, 2f, 0f));
        SceneNode brick = level.Part("Brick", new Vector3(0f, 2f, 0f));
        SceneNode ember = level.Part("Ember", new Vector3(-4f, 2f, 0f), brick);

        ember.Tags = TagSet.Of("Lava");
        brick.Tags = TagSet.Of("Lava", "Hot stuff");
        pool.Tags = TagSet.Of("Lava");

        brick.Attributes = AttributeSet.Empty
            .With("Damage", MemberValue.Of(25d))
            .With("Label", MemberValue.Of("hot"))
            .With("Deadly", MemberValue.Of(true))
            .With("Push", MemberValue.Of(new Vector3(0f, 12.5f, -1f)))
            .With("Glow", MemberValue.Of(new Color3(1f, 0.5f, 0f)))
            .With("Rest", MemberValue.Of(new CFrame(1f, 2f, 3f)));

        return (brick, pool, ember);
    }
}
