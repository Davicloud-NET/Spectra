using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The touch of a solid part by a player the engine's own mover walks: what
/// the mover runs into is touched, however far its slide has carried the
/// player by the end of the tick.
/// </summary>
public sealed class PartTouchWalkTests
{
    private const float Dt = Touching.Dt;

    private readonly List<string> _log = [];

    private string[] Heard => [.. _log.Where(line => line.StartsWith("hear touch", StringComparison.Ordinal))];

    // The post's corner is cornerZ to the side of a walk along +X at Z zero,
    // so a capsule with a radius of 0.35 clips it and is turned aside. The
    // faster the player, the further the slide ends from the corner.
    [Theory]
    [InlineData(0.30f, 1f, false)]
    [InlineData(0.34f, 1f, true)]
    [InlineData(0.20f, 1f, true)]
    [InlineData(0.30f, 2f, true)]
    [InlineData(0.30f, 4f, true)]
    public void A_corner_the_player_glances_off_is_touched_at_any_speed_and_from_any_start(
        float cornerZ, float walkSpeedScale, bool isSprinting)
    {
        var missed = new List<float>();

        // A start a little further on each time, so the tick that hits the
        // corner hits it early, late and in between.
        for (float start = 0f; start < 0.5f; start += 0.02f)
        {
            Walk walk = WalkPastPost(start, cornerZ, walkSpeedScale, isSprinting);

            walk.TurnedAsideBy.ShouldBeGreaterThan(0.001f, $"the post did not turn the player aside from {start}");
            if (walk.Heard.Length == 0)
                missed.Add(start);
            else
                walk.Heard.ShouldBe(["hear touch 1 by 0", "hear touch 2 by 0"], $"from {start}");
        }

        missed.ShouldBeEmpty();
    }

    [Fact]
    public void A_post_the_player_passes_three_centimetres_clear_of_is_not_touched()
    {
        Walk walk = WalkPastPost(0f, FakePlayerPresence.Radius + 0.03f, 1f, isSprinting: true);

        walk.TurnedAsideBy.ShouldBe(0f, 1e-4f);
        walk.Heard.ShouldBeEmpty();
    }

    private Walk WalkPastPost(float start, float cornerZ, float walkSpeedScale, bool isSprinting)
    {
        _log.Clear();
        var scene = new Scene("Touch");
        Part(scene, "floor", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 0.5f, 10f));
        SceneNode post = Part(scene, "post", new Vector3(6f, 1f, cornerZ + 0.5f), new Vector3(0.5f, 1f, 0.5f));

        var world = new EntityWorld(scene, new CapturingLogger(), Touching.Catalog(_log));
        world.Activate();
        world.Scripts = new FakeScriptRuntime(_log);
        world.Listeners.Add(post, TouchSignal.Touched, 1);
        world.Listeners.Add(post, TouchSignal.TouchEnded, 2);

        var character = new CharacterSimulation(scene) { SpawnPosition = new Vector3(start, 0.05f, 0f) };
        character.Tuning.WalkSpeed *= walkSpeedScale;
        character.Spawn();
        world.Player = character;

        var command = new CharacterCommand
        {
            MoveForward = CharacterCommand.Axis(1f),
            Buttons = isSprinting ? CharacterButtons.Sprint : CharacterButtons.None,
        };

        for (int tick = 0; tick < 600 && character.State.Position.X < 9f; tick++)
        {
            world.Tick(Dt);
            character.Tick(in command, Dt);
        }

        // Past the post by now, and a few ticks more for the touch to end.
        character.State.Position.X.ShouldBeGreaterThanOrEqualTo(9f);
        for (int tick = 0; tick < 10; tick++)
            world.Tick(Dt);

        return new Walk(Heard, -character.State.Position.Z);
    }

    private static SceneNode Part(Scene scene, string name, Vector3 center, Vector3 half)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = center;

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(-half, half);
        return node;
    }

    private readonly record struct Walk(string[] Heard, float TurnedAsideBy);
}
