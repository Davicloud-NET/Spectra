using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The spinner of the demo level and its script, read from the engine's own
/// content the way the demo reads it.
/// </summary>
public sealed class DemoSpinnerTests
{
    [Fact]
    public void The_spinner_says_one_line_turns_a_radian_a_second_and_is_back_after_Stop()
    {
        using var level = new ScriptLevel();
        level.Manager.Scripts.Source = null;
        SceneNode spinner = DemoSpinner.Build(level.Scene, MaterialRef.Default);
        Transform authored = spinner.LocalTransform;

        level.Play().Tick(60);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Spinner turns on a script"]);
        CFrame turned = new CFrame(DemoSpinner.Position) * CFrame.Angles(0f, 1f, 0f);
        spinner.CFrame.FuzzyEq(turned, 1e-4f).ShouldBeTrue($"the spinner is at {spinner.CFrame}");

        level.Stop();

        spinner.LocalPosition.ShouldBe(authored.Position);
        spinner.LocalRotation.ShouldBe(authored.Rotation);
    }

    [Fact]
    public void The_spinner_is_only_to_look_at()
    {
        var scene = new Scene("Level");

        SceneNode spinner = DemoSpinner.Build(scene, MaterialRef.Default);

        spinner.BrushKind.ShouldBe(BrushKind.Part);
        spinner.CanCollide.ShouldBeFalse();
        spinner.CanQuery.ShouldBeFalse();
        SceneNode script = spinner.Children.ShouldHaveSingleItem();
        script.Script.ShouldNotBeNull().Path.ShouldBe(DemoSpinner.ScriptPath);
    }
}
