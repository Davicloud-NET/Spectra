using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The mover in Luau from a pack. Its files are content, so a cook carries
/// them, and the cooked course is walked by it as the loose one is.
/// </summary>
[Trait("Suite", RealScriptCheck.Suite)]
public class CookedMoverTests
{
    private const string Installed = "The player moves by a mover in Luau";

    // As a cooked level is held to the authored one under the built-in mover.
    private const float Tolerance = 1e-4f;

    private const float East = 0f;
    private const float North = -MathF.PI / 2f;

    // Out of the start room through its door, up the stairs, through the
    // doorway and off the terrace, then up the ramp and off its platform.
    private static readonly (CharacterCommand Command, int Ticks)[] Legs =
    [
        (default, 20),
        (Walk(East), 335),
        (Walk(North, CharacterButtons.Sprint), 140),
        (Walk(East, CharacterButtons.Jump), 1),
        (Walk(East), 200),
        (default, 30),
    ];

    private static readonly Lazy<Played> BothRuns = new(PlayBoth);

    [Fact]
    public void The_pack_carries_the_mover_and_the_module_it_is_asked_for_by()
    {
        Played played = Play();

        played.CookedHas.ShouldBe(MoverProject.Scripts);
    }

    [Fact]
    public void The_level_installs_the_mover_from_loose_files_and_from_the_pack_and_nothing_goes_wrong()
    {
        Played played = Play();

        played.LooseLines.ShouldBe([$"Print {DemoMover.ScriptPath}:11: {Installed}"]);
        played.CookedLines.ShouldBe(played.LooseLines);
        played.LooseMoved.ShouldBeTrue("a script's mover moved the loose level's player");
        played.CookedMoved.ShouldBeTrue("a script's mover moved the cooked level's player");
    }

    [Fact]
    public void The_cooked_course_is_walked_by_the_mover_as_the_loose_one_is()
    {
        Played played = Play();

        played.Cooked.Count.ShouldBe(played.Loose.Count);
        for (int tick = 0; tick < played.Loose.Count; tick++)
        {
            Vector3.Distance(played.Cooked[tick].Position, played.Loose[tick].Position).ShouldBeLessThan(
                Tolerance, $"tick {tick}: cooked {played.Cooked[tick].Position}, loose {played.Loose[tick].Position}");
            played.Cooked[tick].Grounded.ShouldBe(played.Loose[tick].Grounded, $"tick {tick}");
        }
    }

    // The walk is worth comparing only when it went somewhere.
    [Fact]
    public void The_walk_climbs_the_stairs_falls_off_the_terrace_and_climbs_the_ramp()
    {
        Played played = Play();

        played.Cooked.Count(state => state.SteppedUpBy > 0.2f).ShouldBeGreaterThanOrEqualTo(5);
        played.Cooked.ShouldContain(state => !state.Grounded && state.Velocity.Y < -4f);
        played.Cooked.ShouldContain(state => state.Grounded && state.GroundNormal.Y < 0.95f);
        played.Cooked[^1].Position.X.ShouldBeGreaterThan(160f);
        played.Cooked[^1].Grounded.ShouldBeTrue();
    }

    private static CharacterCommand Walk(float yaw, CharacterButtons buttons = CharacterButtons.None) =>
        new() { MoveForward = CharacterCommand.Axis(1f), Yaw = yaw, Buttons = buttons };

    // Skips where Luau is not built. The level is cooked and played once for
    // the whole class.
    private static Played Play()
    {
        RealScriptCheck.Require();
        return BothRuns.Value;
    }

    private static Played PlayBoth()
    {
        using TempProject project = MoverProject.Write();

        // OpenGL only: that is what the fake renderer reports.
        CookResult cook = new CookSession(
            project.Layout,
            new CookSettings
            {
                UseCache = false,
                Targets = [GraphicsBackend.OpenGL],
                ScriptCheck = new SpectraEngine.Scripting.Luau.LuauSyntaxCheck(),
            }).Run();

        cook.Succeeded.ShouldBeTrue(string.Join(Environment.NewLine, cook.Diagnostics));

        List<CharacterState> loose;
        string[] looseLines;
        bool looseMoved;
        using (PlayedCourse level = PlayedCourse.Loose(project, MoverProject.Bundle))
        {
            loose = level.Walk(Legs);
            looseLines = level.Lines.All;
            looseMoved = !ReferenceEquals(level.Character.Mover, DefaultCharacterMover.Instance);
            level.Scene.HasCompiledStaticWorld.ShouldBeFalse();
        }

        using PlayedCourse cooked = PlayedCourse.Cooked(project, MoverProject.Bundle);
        List<CharacterState> walked = cooked.Walk(Legs);
        cooked.Scene.HasCompiledStaticWorld.ShouldBeTrue();

        return new Played(
            loose, looseLines, looseMoved,
            walked, cooked.Lines.All, !ReferenceEquals(cooked.Character.Mover, DefaultCharacterMover.Instance),
            [.. MoverProject.Scripts.Where(cooked.Has)]);
    }

    private sealed record Played(
        List<CharacterState> Loose,
        string[] LooseLines,
        bool LooseMoved,
        List<CharacterState> Cooked,
        string[] CookedLines,
        bool CookedMoved,
        string[] CookedHas);
}
