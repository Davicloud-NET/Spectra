using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Packs;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Scripting.Luau;
using System;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// One level played twice, from its loose files and from its cooked pack. The
/// script under each of its nodes asks about everything a script can ask, and
/// both runs have to print the same lines.
/// </summary>
// Where the two differ, the cooked level is what is wrong: it has to answer
// as the level somebody built does.
[Trait("Suite", RealScriptCheck.Suite)]
public class LooseAndCookedLevelTests
{
    // Long enough for the deepest script to ask everything, wait and count
    // three heartbeats.
    private const int Ticks = 90;

    private static readonly Lazy<Played> BothRuns = new(PlayBoth);

    [Fact]
    public void A_level_played_from_its_pack_prints_what_it_prints_from_its_loose_files()
    {
        Played played = Play();

        played.Cooked.ShouldBe(played.Loose);
    }

    [Fact]
    public void Every_script_ran_to_its_end_and_no_line_was_dropped()
    {
        Played played = Play();

        foreach (string asker in AskedLevel.Askers)
        {
            played.Loose.ShouldContain(line => line.Contains($"{asker} | heartbeat | 3 beats of "), asker);
            played.Loose.ShouldContain(line => line.Contains($"{asker} | waited | "), asker);
            played.Loose.ShouldContain(line => line.EndsWith($"{asker} | asked | everything", StringComparison.Ordinal), asker);
        }

        // A dropped line is a warning. The one error is the script with no file.
        played.Loose.ShouldNotContain(line => line.StartsWith("Warning", StringComparison.Ordinal));
        played.Loose.Count(line => line.StartsWith("Error", StringComparison.Ordinal)).ShouldBe(1);
        played.Loose.Length.ShouldBeGreaterThan(AskedLevel.Askers.Length * 40);
    }

    [Theory]
    [InlineData("Floor | up 1 | Floor is a Part")]
    [InlineData("Doorway | up 1 | Doorway is a Part")]
    [InlineData("Plank | up 2 | Shelf is a Folder")]
    [InlineData("Crate | up 1 | Crate is a Part")]
    [InlineData("Door | up 1 | Door is a func_door")]
    [InlineData("Relay | up 1 | Relay is a logic_relay")]
    [InlineData("Sun | up 1 | Sun is a DirectionalLight")]
    [InlineData("Spot | up 2 | Lamp is a PointLight")]
    [InlineData("Prop | up 1 | Prop is a MeshPart")]
    [InlineData("Later | up 1 | Later is a Script")]
    [InlineData("Rules | up 1 | Rules is a ModuleScript")]
    [InlineData("Ask | up 0 | Ask is a Script")]
    [InlineData("Root | up 1 | Root is a Folder")]
    public void A_node_has_one_class_name_in_both_levels(string said)
    {
        Played played = Play();

        played.Loose.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
        played.Cooked.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
    }

    // A world brush has no brush once it is baked, and a mesh the pack does
    // not bind has no renderer. Each is still what it was.
    [Theory]
    [InlineData("Floor | is a | Instance BasePart Part")]
    [InlineData("Doorway | is a | Instance BasePart Part")]
    [InlineData("Plank | is a | Instance BasePart Part")]
    [InlineData("Crate | is a | Instance BasePart Part")]
    [InlineData("Bulb | is a | Instance BasePart Part")]
    [InlineData("Door | is a | Instance BasePart Part func_door")]
    [InlineData("Relay | is a | Instance logic_relay")]
    [InlineData("Start | is a | Instance info_player_start")]
    [InlineData("Sun | is a | Instance Light DirectionalLight")]
    [InlineData("Spot | is a | Instance Light SpotLight")]
    [InlineData("Prop | is a | Instance BasePart MeshPart")]
    [InlineData("Shelf | is a | Instance Folder")]
    [InlineData("Later | is a | Instance Script")]
    [InlineData("Rules | is a | Instance ModuleScript")]
    [InlineData("Ask | is a | Instance Script")]
    [InlineData("Root | is a | Instance Folder")]
    public void A_node_is_of_the_same_classes_in_both_levels(string said)
    {
        Played played = Play();

        played.Loose.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
        played.Cooked.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Shelf | children | 2: Plank Part, Ask Script")]
    [InlineData("Props | children | 2: Prop MeshPart, Ask Script")]
    [InlineData("Shelf | descendants | 3: Plank Part, Ask Script, Ask Script")]
    [InlineData("Shelf | first part below | Root.Shelf.Plank")]
    [InlineData("Props | first part below | Root.Props.Prop")]
    [InlineData("Root | first part below | Root.Floor")]
    [InlineData("Root | first light below | Root.Sun")]
    [InlineData("Root | script child | Root.Later")]
    [InlineData("Plank | part above | Root.Shelf.Plank")]
    [InlineData("Plank | folder above | Root.Shelf")]
    [InlineData("Spot | first of its name | Root.Lamp.Spot")]
    [InlineData("Ask | full name | Root.Crate.Ask.Ask")]
    public void A_search_of_the_tree_finds_the_same_node_in_both_levels(string said)
    {
        Played played = Play();

        played.Loose.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
        played.Cooked.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
    }

    [Fact]
    public void The_root_has_its_children_in_one_order_in_both_levels()
    {
        Played played = Play();

        string loose = played.Loose.Single(line => line.Contains("Root | children | "));
        string cooked = played.Cooked.Single(line => line.Contains("Root | children | "));

        cooked.ShouldBe(loose);
        loose.ShouldContain("Floor Part, Wall Part, BackWall Part, Doorway Part, Crate Part, Door func_door,");
    }

    [Fact]
    public void Every_script_finds_its_own_node_by_name_and_waits_only_for_a_child_that_is_not_there()
    {
        Played played = Play();

        foreach (string[] lines in new[] { played.Loose, played.Cooked })
        {
            foreach (string asker in AskedLevel.Askers)
            {
                lines.ShouldContain(line => line.EndsWith($"{asker} | child by name | true", StringComparison.Ordinal), asker);
                lines.ShouldContain(line => line.EndsWith($"{asker} | workspace above | true", StringComparison.Ordinal), asker);
                lines.ShouldContain(line => line.EndsWith($"{asker} | in the tree | true", StringComparison.Ordinal), asker);
                lines.ShouldContain(line => line.EndsWith($"{asker} | a child that is there | true", StringComparison.Ordinal), asker);
                lines.ShouldContain(line => line.EndsWith($"{asker} | a child that never comes | nil", StringComparison.Ordinal), asker);
            }
        }
    }

    [Theory]
    [InlineData("Floor")]
    [InlineData("Doorway")]
    [InlineData("Plank")]
    [InlineData("Shelf")]
    [InlineData("Root")]
    public void A_world_brush_and_what_is_above_it_refuse_a_scripts_move_in_the_same_words(string node)
    {
        Played played = Play();

        string loose = played.Loose.Single(line => line.Contains($"{node} | move up | "));
        string cooked = played.Cooked.Single(line => line.Contains($"{node} | move up | "));

        cooked.ShouldBe(loose);
        loose.ShouldContain("cannot be moved: a world brush sits at or below it");
    }

    [Theory]
    [InlineData("Crate")]
    [InlineData("Leaf")]
    [InlineData("Prop")]
    [InlineData("Sun")]
    [InlineData("Door")]
    public void A_part_a_mesh_a_light_and_an_entity_take_a_scripts_move_in_both_levels(string node)
    {
        Played played = Play();

        played.Loose.ShouldContain(line => line.EndsWith($"{node} | move up | done", StringComparison.Ordinal));
        played.Cooked.ShouldContain(line => line.EndsWith($"{node} | move up | done", StringComparison.Ordinal));
    }

    [Fact]
    public void A_script_with_no_file_fails_to_start_the_same_way_in_both_levels()
    {
        Played played = Play();

        string loose = played.Loose.Single(line => line.StartsWith("Error", StringComparison.Ordinal));
        string cooked = played.Cooked.Single(line => line.StartsWith("Error", StringComparison.Ordinal));

        cooked.ShouldBe(loose);
        loose.ShouldContain(AskedLevel.LostScript);
    }

    [Fact]
    public void A_name_outside_ASCII_comes_through_the_pack_as_it_was_written()
    {
        Played played = Play();
        string said = $"{AskedLevel.DoorInGerman} | up 1 | {AskedLevel.DoorInGerman} is a Folder";

        played.Loose.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
        played.Cooked.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
    }

    // By a relative path, by @assets, an init module with @self, and a module
    // node. From a pack every one of them is an entry behind a header.
    [Fact]
    public void A_script_gets_its_modules_by_every_way_of_asking_from_loose_files_and_from_the_pack()
    {
        Played played = Play();

        played.Loose.Where(line => line.Contains(RequiringScript.Mark)).ShouldBe(RequiringScript.Lines);
        played.Cooked.Where(line => line.Contains(RequiringScript.Mark)).ShouldBe(RequiringScript.Lines);
    }

    // By the node's entity class, which a baked level has as its loose one does.
    [Theory]
    [InlineData("Door | member Open | a method")]
    [InlineData("Door | member OnFullyOpen | Signal OnFullyOpen")]
    [InlineData("Door | sent Open | true")]
    [InlineData("Door | heard | OnOpen")]
    [InlineData("Door | heard | OnFullyOpen")]
    [InlineData("Relay | member Trigger | a method")]
    [InlineData("Relay | member OnTrigger | Signal OnTrigger")]
    [InlineData("Relay | heard its own output | 0 at once, 1 a tick later")]
    public void An_entity_takes_an_input_from_a_script_and_a_script_hears_its_output_in_both_levels(string said)
    {
        Played played = Play();

        played.Loose.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
        played.Cooked.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Crate", "Open", "Part")]
    [InlineData("Floor", "OnFullyOpen", "Part")]
    [InlineData("Sun", "Trigger", "DirectionalLight")]
    [InlineData("Start", "Open", "info_player_start")]
    [InlineData("Door", "Trigger", "func_door")]
    [InlineData("Relay", "Open", "logic_relay")]
    public void A_node_refuses_an_input_and_an_output_its_class_does_not_have_in_the_same_words(
        string node, string name, string className)
    {
        Played played = Play();

        string loose = played.Loose.Single(line => line.Contains($"{node} | member {name} | "));
        string cooked = played.Cooked.Single(line => line.Contains($"{node} | member {name} | "));

        cooked.ShouldBe(loose);
        loose.ShouldContain($"{name} is not a valid member of {className} \"{node} renamed\"");
    }

    [Fact]
    public void A_value_for_an_input_that_takes_none_is_refused_the_same_way_in_both_levels()
    {
        Played played = Play();

        string loose = played.Loose.Single(line => line.Contains("Relay | a value for an input that takes none | "));
        string cooked = played.Cooked.Single(line => line.Contains("Relay | a value for an input that takes none | "));

        cooked.ShouldBe(loose);
        loose.ShouldContain("too many arguments to 'Trigger' (no value expected, got 1 value)");
    }

    // A light comes through the pack as a record of its own. What a script
    // reads of it has to be what the level was built with.
    [Theory]
    [InlineData("Sun | has Enabled | true")]
    [InlineData("Sun | has Brightness | 3.5")]
    [InlineData("Lamp | has Brightness | 12")]
    [InlineData("Lamp | has Range | 6.5")]
    [InlineData("Lamp | has Color | 1, 0.797738, 0.484529")]
    [InlineData("Spot | has Range | 9.25")]
    [InlineData("Panel | has Brightness | 6")]
    [InlineData("Panel | has Range | 5")]
    [InlineData("Disc | has Enabled | false")]
    [InlineData("Disc | has Range | 4")]
    [InlineData("Disc | after the light | true 1 ff6d2d 1, 0.425575, 0.174595")]
    [InlineData("Lamp | after the light | false 6 ffa63e 1, 0.649849, 0.242265")]
    public void A_light_reads_and_takes_the_same_in_both_levels(string said)
    {
        Played played = Play();

        played.Loose.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
        played.Cooked.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Sun | has Range | ", "Range is not a valid member of DirectionalLight \"Sun renamed\"")]
    [InlineData("Sun | reach | ", "Range is not a valid member of DirectionalLight \"Sun renamed\"")]
    [InlineData("Crate | has Brightness | ", "Brightness is not a valid member of Part \"Crate renamed\"")]
    [InlineData("Floor | has Enabled | ", "Enabled is not a valid member of Part \"Floor renamed\"")]
    [InlineData("Lamp | has Visible | ", "Visible is not a valid member of PointLight \"Lamp renamed\"")]
    [InlineData("Shelf | hide | ", "Visible is not a valid member of Folder \"Shelf renamed\"")]
    [InlineData("Relay | walk through | ", "CanCollide is not a valid member of logic_relay \"Relay renamed\"")]
    public void A_member_of_another_class_is_refused_in_the_same_words_in_both_levels(string asked, string words)
    {
        Played played = Play();

        string loose = played.Loose.Single(line => line.Contains(asked));
        string cooked = played.Cooked.Single(line => line.Contains(asked));

        cooked.ShouldBe(loose);
        loose.ShouldEndWith(words);
    }

    // The switches are bits of a node's record in the pack. A mesh the pack
    // does not bind and a brush entity keep theirs.
    [Theory]
    [InlineData("Crate | has CanCollide | true")]
    [InlineData("Crate | after the switches | false false false false")]
    [InlineData("Prop | after the switches | false false false false")]
    [InlineData("Door | after the switches | false false false false")]
    [InlineData("Leaf | walk through | done")]
    [InlineData("Bulb | hide | done")]
    [InlineData("Floor | has Visible | true")]
    [InlineData("Floor | after the switches | true true true true")]
    [InlineData("Doorway | after the switches | true true true true")]
    [InlineData("Plank | after the switches | true true true true")]
    public void A_part_and_a_mesh_read_and_take_the_same_switches_in_both_levels(string said)
    {
        Played played = Play();

        played.Loose.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
        played.Cooked.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
    }

    // Baked, a world brush has no brush on its node. It still says it is one.
    [Theory]
    [InlineData("Floor", "walk through", "CanCollide")]
    [InlineData("Floor", "hide", "Visible")]
    [InlineData("Doorway", "hide from rays", "CanQuery")]
    [InlineData("Plank", "stop touching", "CanTouch")]
    public void A_world_brush_refuses_a_switch_in_the_same_words_in_both_levels(string node, string asked, string member)
    {
        Played played = Play();

        string loose = played.Loose.Single(line => line.Contains($"{node} | {asked} | "));
        string cooked = played.Cooked.Single(line => line.Contains($"{node} | {asked} | "));

        cooked.ShouldBe(loose);
        loose.ShouldEndWith(
            $"{node} renamed cannot change its {member}: it is a world brush, fused into the level, and only " +
            "parts can change while a level plays. Convert the brush to a part.");
    }

    // A keyvalue is text in the map and in the pack. Its type comes from the
    // class, which both levels run with.
    [Theory]
    [InlineData("Door | attribute speed | number 100")]
    [InlineData("Door | attribute movedir | Vector3 0, 1, 0")]
    [InlineData("Door | attribute startopen | boolean false")]
    [InlineData("Door | attribute wait | number 4")]
    [InlineData("Door | attribute tag | nil nil")]
    [InlineData("Relay | attribute startdisabled | boolean false")]
    [InlineData("Relay | attribute tag | string first")]
    [InlineData("Relay | attribute spawnflags | string 1")]
    [InlineData("Relay | attribute nothing | nil nil")]
    [InlineData("Start | attribute speed | nil nil")]
    [InlineData("Crate | attribute speed | nil nil")]
    public void An_attribute_reads_the_same_in_both_levels(string said)
    {
        Played played = Play();

        played.Loose.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
        played.Cooked.ShouldContain(line => line.EndsWith(said, StringComparison.Ordinal));
    }

    // A part keeps its brush in a baked level, and a touch is found from
    // that. So the player lands on the same pad on the same tick in both.
    [Fact]
    public void A_script_hears_the_same_touches_in_the_same_order_from_loose_files_and_from_the_pack()
    {
        Played played = Play();

        string[] loose = [.. played.Loose.Where(line => line.Contains(TouchingScript.Mark)).Select(TouchingScript.Said)];
        string[] cooked = [.. played.Cooked.Where(line => line.Contains(TouchingScript.Mark)).Select(TouchingScript.Said)];

        cooked.ShouldBe(loose);
        loose.Length.ShouldBe(TouchingScript.Lines.Length, string.Join(Environment.NewLine, loose));
        for (int i = 0; i < loose.Length; i++)
        {
            (string what, string answer) = TouchingScript.Lines[i];
            loose[i].ShouldStartWith(what + " | ");
            loose[i].ShouldEndWith(answer);
        }
    }

    [Theory]
    [InlineData("Crate")]
    [InlineData("Leaf")]
    [InlineData("Bulb")]
    [InlineData("Door")]
    [InlineData("Prop")]
    public void A_part_a_mesh_and_a_brush_entity_take_a_listener_for_a_touch_in_both_levels(string node)
    {
        Played played = Play();

        foreach (string[] lines in new[] { played.Loose, played.Cooked })
        {
            lines.ShouldContain(line => line.EndsWith($"{node} | listen for a touch | done", StringComparison.Ordinal));
            lines.ShouldContain(line => line.EndsWith(
                $"{node} | the signal of a touch | Signal Touched true RBXScriptSignal", StringComparison.Ordinal));

            // Each was moved away and has its CanTouch off by then.
            lines.ShouldNotContain(line => line.Contains($"{node} | touched | "));
        }
    }

    // Baked, a world brush has no brush on its node, and a touch of it can
    // be told in neither level.
    [Theory]
    [InlineData("Floor")]
    [InlineData("Doorway")]
    [InlineData("Plank")]
    public void A_world_brush_refuses_a_listener_for_a_touch_in_the_same_words_in_both_levels(string node)
    {
        Played played = Play();

        string loose = played.Loose.Single(line => line.Contains($"{node} | listen for a touch | "));
        string cooked = played.Cooked.Single(line => line.Contains($"{node} | listen for a touch | "));

        cooked.ShouldBe(loose);
        loose.ShouldEndWith(
            $"{node} renamed cannot tell what touches it: it is a world brush, fused into the level, and only " +
            "parts notice a touch while a level plays. Convert the brush to a part.");
    }

    [Theory]
    [InlineData("Sun", "DirectionalLight")]
    [InlineData("Shelf", "Folder")]
    [InlineData("Relay", "logic_relay")]
    [InlineData("Later", "Script")]
    public void A_node_with_no_shape_has_no_touch_to_listen_for_in_the_same_words_in_both_levels(
        string node, string className)
    {
        Played played = Play();

        string loose = played.Loose.Single(line => line.Contains($"{node} | listen for a touch | "));
        string cooked = played.Cooked.Single(line => line.Contains($"{node} | listen for a touch | "));

        cooked.ShouldBe(loose);
        loose.ShouldEndWith($"Touched is not a valid member of {className} \"{node} renamed\"");
    }

    // A tag's nodes come in the order of the tree, which a baked level has
    // to keep, and a world brush holds a tag and an attribute though its
    // node has no brush left.
    [Fact]
    public void Tags_and_attributes_come_out_the_same_from_loose_files_and_from_the_pack()
    {
        Played played = Play();

        played.Loose.Where(line => line.Contains(TaggingScript.Mark)).ShouldBe(TaggingScript.Lines);
        played.Cooked.Where(line => line.Contains(TaggingScript.Mark)).ShouldBe(TaggingScript.Lines);
    }

    // A made node, a clone of a part, a mesh, a light, a door and a script,
    // another parent and Destroy. A baked level has no brush on a world
    // brush's node and binds no mesh, and still answers each as the loose one.
    [Fact]
    public void What_a_script_makes_clones_moves_and_destroys_comes_out_the_same_from_loose_files_and_from_the_pack()
    {
        Played played = Play();

        played.Loose.Where(line => line.Contains(MakingScript.Mark)).ShouldBe(MakingScript.Lines);
        played.Cooked.Where(line => line.Contains(MakingScript.Mark)).ShouldBe(MakingScript.Lines);
    }

    // Skips where Luau is not built. The level is cooked and played once for
    // the whole class.
    private static Played Play()
    {
        RealScriptCheck.Require();
        return BothRuns.Value;
    }

    private static Played PlayBoth()
    {
        using TempProject project = AskedLevel.Write();

        // OpenGL only: that is what the fake renderer reports.
        CookResult cook = new CookSession(
            project.Layout,
            new CookSettings
            {
                UseCache = false,
                Targets = [GraphicsBackend.OpenGL],
                ScriptCheck = new LuauSyntaxCheck(),
            }).Run();

        cook.Succeeded.ShouldBeTrue(string.Join(Environment.NewLine, cook.Diagnostics));

        string[] loose;
        using (PlayedLevel level = PlayedLevel.Loose(project, AskedLevel.Bundle))
        {
            loose = level.Play(Ticks).Lines.All;

            // The loose level is the authored one: its walls are brushes.
            level.Scene.Root.Traverse().ShouldContain(node => node.IsStaticWorldBrush);
            level.Scene.HasCompiledStaticWorld.ShouldBeFalse();
        }

        string[] cooked;
        using (PlayedLevel level = PlayedLevel.Cooked(project, AskedLevel.Bundle))
        {
            cooked = level.Play(Ticks).Lines.All;

            // And the cooked one is baked: no node carries a world brush.
            level.Scene.Root.Traverse().ShouldNotContain(node => node.IsStaticWorldBrush);
            level.Scene.HasCompiledStaticWorld.ShouldBeTrue();
        }

        return new Played(loose, cooked);
    }

    private sealed record Played(string[] Loose, string[] Cooked);
}
