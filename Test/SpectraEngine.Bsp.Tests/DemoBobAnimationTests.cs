using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The demo's bobbing brush must yield to edits: it runs after the editor
/// each frame, so it would otherwise overwrite every drag and undo.
/// </summary>
public sealed class DemoBobAnimationTests
{
    private const float Amplitude = 0.5f;
    private const double PeriodSeconds = 4.0;

    // A quarter period, where the bob is at full amplitude.
    private const double PeakTime = PeriodSeconds / 4.0;

    [Fact]
    public void The_bob_moves_the_node_when_nobody_else_does()
    {
        (SceneNode node, DemoBobAnimation bob) = CreateBob(new Vector3(-2f, 0.1f, -2f));

        bob.Advance(PeakTime);

        node.LocalPosition.Y.ShouldBe(0.1f + Amplitude, 1e-5f);
        node.LocalPosition.X.ShouldBe(-2f);
        node.LocalPosition.Z.ShouldBe(-2f);
    }

    [Fact]
    public void An_external_edit_survives_the_next_frame()
    {
        (SceneNode node, DemoBobAnimation bob) = CreateBob(new Vector3(-2f, 0.1f, -2f));
        bob.Advance(PeakTime);

        var edited = new Vector3(5f, 3f, -7f);
        node.LocalPosition = edited;
        bob.Advance(PeakTime);

        // Same phase as before the edit, so the bob adds nothing new.
        node.LocalPosition.ShouldBe(edited);
    }

    [Fact]
    public void An_external_edit_is_not_snapped_by_the_animations_own_offset()
    {
        // Taking the edited position as the new rest pose would re-add the
        // current bob and jump the node by up to a full amplitude.
        (SceneNode node, DemoBobAnimation bob) = CreateBob(Vector3.Zero);
        bob.Advance(PeakTime);

        var edited = new Vector3(0f, 10f, 0f);
        node.LocalPosition = edited;
        bob.Advance(PeakTime + 0.001);

        float jump = Vector3.Distance(node.LocalPosition, edited);
        jump.ShouldBeLessThan(0.01f);
    }

    [Fact]
    public void The_bob_carries_on_from_the_edited_pose()
    {
        (SceneNode node, DemoBobAnimation bob) = CreateBob(Vector3.Zero);
        bob.Advance(PeakTime);          // +amplitude
        node.LocalPosition = new Vector3(0f, 10f, 0f);
        bob.Advance(PeakTime);          // adopt

        bob.Advance(PeakTime * 3.0);    // three quarter periods on: -amplitude

        // The edit landed on a peak, so the new rest pose is 10 - amplitude.
        node.LocalPosition.Y.ShouldBe(10f - 2f * Amplitude, 1e-4f);
    }

    [Fact]
    public void An_undo_back_to_the_original_pose_is_honoured_too()
    {
        // Undo writes back rest pose plus the bob at capture time. That must
        // not be mistaken for the animation's own write.
        (SceneNode node, DemoBobAnimation bob) = CreateBob(Vector3.Zero);
        bob.Advance(PeakTime);
        Vector3 captured = node.LocalPosition;

        node.LocalPosition = new Vector3(4f, 4f, 4f);
        bob.Advance(PeakTime);
        node.LocalPosition = captured;
        bob.Advance(PeakTime);

        node.LocalPosition.ShouldBe(captured);
    }

    [Fact]
    public void The_bob_keeps_dirtying_the_static_world()
    {
        // The bob exists to exercise the async recompile every frame.
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("PillarA");
        node.Brush = Brush.CreateBox(
            new Vector3(-0.2f, -1.1f, -0.2f), new Vector3(0.2f, 1.1f, 0.2f));
        var bob = new DemoBobAnimation(node, Amplitude, PeriodSeconds);
        scene.RebuildStaticWorld(new FakeRenderer());
        scene.StaticWorldDirty.ShouldBeFalse();

        bob.Advance(PeakTime);

        scene.StaticWorldDirty.ShouldBeTrue();
    }

    private static (SceneNode Node, DemoBobAnimation Bob) CreateBob(Vector3 rest)
    {
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("PillarA");
        node.LocalPosition = rest;
        return (node, new DemoBobAnimation(node, Amplitude, PeriodSeconds));
    }
}
