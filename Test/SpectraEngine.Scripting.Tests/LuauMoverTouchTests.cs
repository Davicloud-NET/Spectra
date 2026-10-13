using System.Numerics;
using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Touch with the mover in Luau. A part is touched by what the mover leaves
/// in the state: the part the player stands on, and the parts its slide
/// stopped at. A mover that left those out would touch late, or not at all.
/// </summary>
public sealed class LuauMoverTouchTests
{
    private const string Installs = """
        game:GetService("Players").LocalPlayer.Character:SetMover(require("./movers/walk"))
        """;

    private const int Patience = 240;

    // The player walks along +X at Z zero, and its capsule has a radius of
    // 0.35. The brick's corner is 0.3 to the side of that line.
    private const float CornerZ = 0.3f;

    [Fact]
    public void A_pad_the_player_walks_over_and_a_wall_it_walks_into_are_touched_on_the_ticks_the_built_in_mover_touches_them()
    {
        string[] expected = WalkOverAPadIntoAWall(isScripted: false);
        string[] walked = WalkOverAPadIntoAWall(isScripted: true);

        walked.ShouldBe(expected);
        walked.Select(line => line[(line.IndexOf(' ') + 1)..]).ShouldBe(
        [
            "Pad touched true",
            "Pad left true",
            "Wall touched true",
        ]);
    }

    // Off a corner the slide ends the tick clear of the brick, the further
    // the faster the player is. Only what the mover said it slid along
    // tells the brick it was touched.
    [Theory]
    [InlineData(4.5f)]
    [InlineData(18f)]
    public void A_kill_brick_the_player_clips_by_its_corner_kills_at_any_walk_speed(float walkSpeed)
    {
        var missed = new List<float>();
        for (float brickX = 6f; brickX < 6.5f; brickX += 0.04f)
        {
            if (!IsKilledAt(brickX, walkSpeed))
                missed.Add(brickX);
        }

        missed.ShouldBeEmpty();
    }

    // Each line with the tick it was printed on in front.
    private static string[] WalkOverAPadIntoAWall(bool isScripted)
    {
        using var level = new ScriptLevel();
        MoverLevel.AddShippedMovers(level.Files);
        level.AddScript(TouchParts.Pad(level, "Pad", 4f), TouchParts.Reporter);
        level.AddScript(TouchParts.Wall(level, "Wall", 9f, isSolid: true), TouchParts.Reporter);
        if (isScripted)
            level.AddScript(level.Scene.Root, Installs);

        level.Play();
        var lines = new List<string>();
        for (int tick = 1; tick <= Patience; tick++)
        {
            int before = level.Printed.Length;
            Tick(level, Forward());
            lines.AddRange(level.Printed[before..].Select(line => $"{tick} {line}"));
        }

        level.Errors.ShouldBeEmpty();
        return [.. lines];
    }

    private static bool IsKilledAt(float brickX, float walkSpeed)
    {
        using var level = new ScriptLevel();
        MoverLevel.AddShippedMovers(level.Files);
        TouchParts.Box(level, "Brick", new Vector3(brickX, 1f, CornerZ + 0.5f), new Vector3(0.5f, 1f, 0.5f));
        level.AddScript(level.Scene.Root, $$"""
            local character = game:GetService("Players").LocalPlayer.Character
            character:SetMover(require("./movers/walk"))
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
