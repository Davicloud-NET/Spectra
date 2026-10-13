using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a script hears of a touch that ends because the player has left the
/// level, as when a host takes its player out while the level plays on.
/// </summary>
public sealed class TouchLeavingTests
{
    [Fact]
    public void A_touch_that_ends_because_the_player_left_hands_over_the_character_which_has_left_by_then()
    {
        using var level = new ScriptLevel();
        SceneNode pad = TouchParts.Pad(level, "Pad", x: 0f);
        level.AddScript(pad, """
            local Players = game:GetService("Players")
            local character = Players.LocalPlayer.Character
            local pad = script.Parent

            pad.Touched:Connect(function(hit)
                print("touched", hit == character, hit.IsGrounded)
            end)
            pad.TouchEnded:Connect(function(hit)
                print("left", hit == character, Players.LocalPlayer)
                print(pcall(function() return hit.Position end))
            end)
            """);
        level.Play().Tick(30);
        level.Printed.ShouldBe(["touched true true"]);

        level.Entities.Player = null;
        level.Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.Length.ShouldBe(3);
        level.Printed[1].ShouldBe("left true nil");
        level.Printed[2].ShouldStartWith("false ");
        level.Printed[2].ShouldEndWith("this object has left the level");
        level.Entities.Touches.PartTouchCount.ShouldBe(0);
    }
}
