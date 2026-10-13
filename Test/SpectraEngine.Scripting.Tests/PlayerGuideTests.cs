using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The site's page on a kill brick and checkpoints, held to the code: its
/// scripts are samples luau-lsp checks, and a player who walks by commands
/// is sent back, remembered and sped up as the page says.
/// </summary>
public sealed class PlayerGuideTests
{
    private const string Folder = "Guides/the_player/";
    private const string Humanoid = ScriptSamples.MistakesFolder + "/kill_brick_humanoid.luau";
    private const int Patience = 600;

    private static readonly SitePage Page = SitePage.At("guides/kill-brick-and-checkpoints.mdx");

    private static string Code(string name) => ScriptSamples.At(Folder + name).Code;

    [Fact]
    public void The_page_shows_its_samples_in_the_order_a_person_writes_them()
    {
        Page.Blocks("luau").ShouldBe(
        [
            Code("kill_brick.luau"), ScriptSamples.At(Humanoid).Code, Code("checkpoints.luau"), Code("speed_pad.luau"),
        ]);
    }

    [Fact]
    public void The_kill_brick_sends_the_player_back_to_the_start()
    {
        using var level = new ScriptLevel();
        level.ScriptAt("Scripts/Lava.luau", Code("kill_brick.luau"), under: TouchParts.Pad(level, "Lava", x: 4f));
        level.Play();
        CharacterSimulation walker = Walker(level);

        WalkUntil(level, () => walker.TryTakeTeleport(out _));

        level.Errors.ShouldBeEmpty();
        Vector3.Distance(walker.State.Position, walker.SpawnPosition).ShouldBeLessThan(0.2f);
    }

    [Fact]
    public void The_kill_brick_of_a_Roblox_script_fails_at_the_touch_with_the_line_the_page_quotes()
    {
        using var level = new ScriptLevel();
        level.ScriptAt("Scripts/Lava.luau", ScriptSamples.At(Humanoid).Code, under: TouchParts.Pad(level, "Lava", x: 4f));
        level.Play();

        WalkUntil(level, () => level.Errors.Length > 0);

        ScriptMessage failure = level.Sink.Messages.First(message => message.Severity == ScriptSeverity.Error);
        failure.Text.ShouldBe("Parent is not a valid member of Character \"Character\"");
        Page.Text.ShouldContain($"`{failure.Text}`");
        Page.Text.ShouldContain($"`Lava.luau:{failure.Line}`");
        ScriptSamples.At(Humanoid).MarkedLines.ShouldBe([failure.Line], "the checker and the level name the same line");
    }

    [Fact]
    public void A_player_who_touches_lava_is_put_back_at_the_checkpoint_they_walked_through()
    {
        using var level = new ScriptLevel();
        TouchParts.Wall(level, "First", x: 3f, isSolid: false).Tags = TagSet.Of("Checkpoint");
        TouchParts.Pad(level, "Pool", x: 7f).Tags = TagSet.Of("Lava");
        level.ScriptAt("Scripts/Script.luau", Code("checkpoints.luau"));
        level.Play();
        CharacterSimulation walker = Walker(level);

        // Through the checkpoint, onto the lava, and back inside the
        // checkpoint, which the player then touches again.
        WalkUntil(level, () => level.Printed.Length >= 2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["reached First", "reached First"]);
        walker.State.Position.X.ShouldBeInRange(3f, 3.2f);
    }

    [Fact]
    public void With_no_checkpoint_passed_the_lava_sends_the_player_to_the_start()
    {
        using var level = new ScriptLevel();
        TouchParts.Pad(level, "Pool", x: 4f).Tags = TagSet.Of("Lava");
        level.ScriptAt("Scripts/Script.luau", Code("checkpoints.luau"));
        level.Play();
        CharacterSimulation walker = Walker(level);

        WalkUntil(level, () => walker.TryTakeTeleport(out _));

        level.Errors.ShouldBeEmpty();
        Vector3.Distance(walker.State.Position, walker.SpawnPosition).ShouldBeLessThan(0.2f);
    }

    [Fact]
    public void A_pad_makes_the_player_as_fast_as_its_attribute_says_while_they_stand_on_it()
    {
        using var level = new ScriptLevel();
        SceneNode pad = TouchParts.Pad(level, "Pad", x: 4f);
        pad.Tags = TagSet.Of("SpeedPad");
        pad.Attributes = AttributeSet.Empty.With("Speed", MemberValue.Of(8d));
        level.ScriptAt("Scripts/Script.luau", Code("speed_pad.luau"));
        level.Play();
        CharacterSimulation walker = Walker(level);
        float built = walker.Tuning.WalkSpeed;

        WalkUntil(level, () => walker.Tuning.WalkSpeed != built);
        walker.Tuning.WalkSpeed.ShouldBe(8f);

        WalkUntil(level, () => walker.Tuning.WalkSpeed == built);
        walker.State.Position.X.ShouldBeGreaterThan(5f, "the player is past the pad");

        level.Errors.ShouldBeEmpty();
        Page.Text.ShouldContain(string.Create(CultureInfo.InvariantCulture, $"A player walks {built} units a second"));
    }

    [Fact]
    public void Stop_puts_back_the_speed_a_pad_gave_the_player()
    {
        using var level = new ScriptLevel();
        TouchParts.Pad(level, "Pad", x: 4f, halfWidth: 3f).Tags = TagSet.Of("SpeedPad");
        level.ScriptAt("Scripts/Script.luau", Code("speed_pad.luau"));
        level.Play();
        CharacterSimulation walker = Walker(level);
        float built = walker.Tuning.WalkSpeed;

        // A pad with no attribute doubles the speed.
        WalkUntil(level, () => walker.Tuning.WalkSpeed != built);
        walker.Tuning.WalkSpeed.ShouldBe(built * 2f);

        level.Stop();

        walker.Tuning.WalkSpeed.ShouldBe(built);
    }

    private static void WalkUntil(ScriptLevel level, Func<bool> isThere)
    {
        for (int tick = 0; tick < Patience; tick++)
        {
            if (isThere())
                return;

            Tick(level, Forward());
        }

        isThere().ShouldBeTrue("the player walked for ten seconds and it did not happen");
    }
}
