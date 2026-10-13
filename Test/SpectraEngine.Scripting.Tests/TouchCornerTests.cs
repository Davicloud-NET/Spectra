using System.Numerics;
using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A kill brick the player clips by its corner, played through a play
/// session with the engine's own mover. The mover turns the player aside
/// there, and the faster the player is, the further off the tick ends.
/// </summary>
public sealed class TouchCornerTests
{
    private const int Patience = 240;

    // The player walks along +X at Z zero, and its capsule has a radius of
    // 0.35. The brick's corner is 0.3 to the side of that line.
    private const float CornerZ = 0.3f;

    [Theory]
    [InlineData(4.5f)]
    [InlineData(9f)]
    [InlineData(18f)]
    public void A_kill_brick_the_player_clips_by_its_corner_kills_at_any_walk_speed(float walkSpeed)
    {
        var missed = new List<float>();

        // A brick a little further on each time, so the tick that hits the
        // corner hits it early, late and in between.
        for (float brickX = 6f; brickX < 6.5f; brickX += 0.04f)
        {
            if (!IsKilledAt(brickX, walkSpeed))
                missed.Add(brickX);
        }

        missed.ShouldBeEmpty();
    }

    private static bool IsKilledAt(float brickX, float walkSpeed)
    {
        using var level = new ScriptLevel();
        TouchParts.Box(level, "Brick", new Vector3(brickX, 1f, CornerZ + 0.5f), new Vector3(0.5f, 1f, 0.5f));
        level.AddScript(level.Scene.Root, $$"""
            local character = game:GetService("Players").LocalPlayer.Character
            character.WalkSpeed = {{walkSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
            workspace.Brick.Touched:Connect(function(hit)
                print("killed")
                hit:Respawn()
            end)
            """);
        level.Play();

        // Until the first kill, or until the player is well past the brick.
        for (int tick = 0; tick < Patience && level.Printed.Length == 0; tick++)
            Tick(level, Forward());

        level.Errors.ShouldBeEmpty();
        if (level.Printed.Length == 0)
            Walker(level).State.Position.X.ShouldBeGreaterThan(brickX + 1f, "the player never got past the brick");

        return level.Printed.Length > 0;
    }
}
