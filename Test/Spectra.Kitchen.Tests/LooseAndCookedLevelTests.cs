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
    private const int Ticks = 45;

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
