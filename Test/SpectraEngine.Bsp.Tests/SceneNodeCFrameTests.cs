using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>A node's local and world CFrame, read and written.</summary>
public sealed class SceneNodeCFrameTests
{
    private const float Quarter = MathF.PI / 2f;
    private const float Loose = 1e-4f;

    // The two frames of the product tests: A * B sits at (7, 7, -1).
    private static readonly CFrame A = new CFrame(1f, 2f, 3f) * CFrame.Angles(0f, Quarter, 0f);
    private static readonly CFrame B = new CFrame(4f, 5f, 6f) * CFrame.Angles(Quarter, 0f, 0f);

    private static readonly CFrame Slanted = new CFrame(-3f, 1.5f, 8f) * CFrame.Angles(0.3f, -0.7f, 1.1f);

    private static (Scene Scene, SceneNode Parent, SceneNode Child) ParentAndChild(CFrame parentFrame)
    {
        var scene = new Scene("Test");
        SceneNode parent = scene.Root.CreateChild("parent");
        parent.LocalCFrame = parentFrame;
        SceneNode child = parent.CreateChild("child");
        return (scene, parent, child);
    }

    // Where the node's own -Z points in the world, read from the engine's matrix.
    private static Vector3 Facing(SceneNode node) =>
        Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, node.WorldMatrix));

    [Fact]
    public void The_engine_composes_a_child_as_its_local_matrix_times_the_parents()
    {
        var (_, parent, child) = ParentAndChild(Slanted);
        child.LocalCFrame = B;

        child.WorldMatrix.ShouldBe(child.LocalTransform.Model * parent.WorldMatrix);
    }

    [Fact]
    public void A_node_turned_by_Angles_about_y_has_the_matrix_of_the_engines_own_turn_about_y()
    {
        var node = new SceneNode("n");
        const float angle = 0.8f;

        node.LocalCFrame = CFrame.Angles(0f, angle, 0f);

        Matrix4x4 engine = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
        node.WorldMatrix.ShouldBe(engine);
    }

    [Fact]
    public void LocalCFrame_reads_the_local_position_and_rotation()
    {
        var node = new SceneNode("n");
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
        node.LocalPosition = new Vector3(1f, 2f, 3f);
        node.LocalRotation = rotation;

        node.LocalCFrame.Position.ShouldBe(new Vector3(1f, 2f, 3f));
        node.LocalCFrame.Rotation.ShouldBe(rotation);
    }

    [Fact]
    public void Setting_LocalCFrame_writes_position_and_rotation_and_keeps_the_scale()
    {
        var node = new SceneNode("n") { LocalScale = new Vector3(2f, 3f, 4f) };

        node.LocalCFrame = Slanted;

        node.LocalPosition.ShouldBe(Slanted.Position);
        node.LocalRotation.ShouldBe(Slanted.Rotation);
        node.LocalScale.ShouldBe(new Vector3(2f, 3f, 4f));
        node.LocalCFrame.ShouldBe(Slanted);
    }

    [Fact]
    public void A_node_with_no_parent_has_its_local_frame_as_its_world_frame()
    {
        var node = new SceneNode("n") { LocalCFrame = Slanted };

        node.CFrame.ShouldBe(Slanted);
    }

    [Fact]
    public void A_childs_world_frame_is_the_parents_frame_times_its_local_frame()
    {
        var (_, _, child) = ParentAndChild(A);

        child.LocalCFrame = B;

        child.CFrame.ShouldHaveComponents(
            7, 7, -1,
            0, 1, 0,
            0, 0, -1,
            -1, 0, 0);
    }

    [Fact]
    public void Without_scale_the_world_frame_is_the_world_matrix()
    {
        var (_, parent, child) = ParentAndChild(Slanted);
        child.LocalCFrame = B;
        SceneNode grandchild = child.CreateChild("grandchild");
        grandchild.LocalCFrame = A;

        grandchild.CFrame.ShouldBeNear(parent.CFrame * child.LocalCFrame * grandchild.LocalCFrame, Loose);
        CFrame.TryFromMatrix(grandchild.WorldMatrix, out CFrame fromMatrix).ShouldBeTrue();
        grandchild.CFrame.ShouldBeNear(fromMatrix, Loose);
        grandchild.CFrame.LookVector.ShouldBeNear(Facing(grandchild), Loose);
    }

    [Fact]
    public void Setting_the_world_frame_under_a_turned_parent_solves_the_local_frame()
    {
        var (_, _, child) = ParentAndChild(A);

        child.CFrame = A * B;

        child.LocalCFrame.ShouldBeNear(B);
    }

    [Fact]
    public void Setting_the_world_frame_lands_the_position_and_the_facing_where_asked()
    {
        var (_, _, child) = ParentAndChild(Slanted);
        var at = new Vector3(10f, -4f, 2f);
        CFrame asked = CFrame.LookAt(at, at + new Vector3(2f, 1f, -2f));

        child.CFrame = asked;

        child.WorldPosition.ShouldBeNear(at, Loose);
        Facing(child).ShouldBeNear(new Vector3(2f, 1f, -2f) / 3f, Loose);
        child.CFrame.ShouldBeNear(asked, Loose);
    }

    [Fact]
    public void Setting_the_world_frame_three_levels_down_lands_where_asked()
    {
        var (_, _, child) = ParentAndChild(Slanted);
        child.LocalCFrame = B;
        SceneNode grandchild = child.CreateChild("grandchild");
        CFrame asked = new CFrame(0.5f, 12f, -7f) * CFrame.Angles(-2.1f, 0.2f, 0.9f);

        grandchild.CFrame = asked;

        grandchild.CFrame.ShouldBeNear(asked, Loose);
        grandchild.WorldPosition.ShouldBeNear(asked.Position, Loose);
        Facing(grandchild).ShouldBeNear(asked.LookVector, Loose);
    }

    [Fact]
    public void Setting_the_world_frame_keeps_the_scale()
    {
        var (_, _, child) = ParentAndChild(Slanted);
        child.LocalScale = new Vector3(2f, 3f, 4f);

        child.CFrame = A;

        child.LocalScale.ShouldBe(new Vector3(2f, 3f, 4f));
        child.WorldPosition.ShouldBeNear(A.Position, Loose);
    }

    [Fact]
    public void Each_write_of_a_new_frame_raises_one_change()
    {
        var (scene, _, child) = ParentAndChild(Slanted);
        List<SceneNode> changed = [];
        scene.NodeTransformChanged += changed.Add;

        child.LocalCFrame = B;
        child.CFrame = A;

        changed.ShouldBe(new[] { child, child });
    }

    [Fact]
    public void Writing_the_local_frame_a_node_already_has_raises_no_change()
    {
        var (scene, _, child) = ParentAndChild(Slanted);
        child.LocalCFrame = B;
        child.LocalScale = new Vector3(2f, 3f, 4f);
        int fired = 0;
        scene.NodeTransformChanged += _ => fired++;

        child.LocalCFrame = child.LocalCFrame;

        fired.ShouldBe(0);
    }

    [Fact]
    public void Writing_the_world_frame_a_node_already_has_raises_no_change()
    {
        var (scene, _, child) = ParentAndChild(Slanted);
        child.LocalCFrame = B;
        Transform before = child.LocalTransform;
        int fired = 0;
        scene.NodeTransformChanged += _ => fired++;

        child.CFrame = child.CFrame;

        fired.ShouldBe(0);
        child.LocalPosition.ShouldBe(before.Position);
        child.LocalRotation.ShouldBe(before.Rotation);
    }

    [Fact]
    public void Writing_the_frame_a_brush_already_has_does_not_dirty_the_static_world()
    {
        var scene = new Scene("Test");
        SceneNode group = scene.Root.CreateChild("group");
        group.LocalCFrame = Slanted;
        SceneNode node = group.CreateChild("brush");
        node.Brush = Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        node.LocalCFrame = B;
        scene.RebuildStaticWorld(new FakeRenderer());
        scene.StaticWorldDirty.ShouldBeFalse();

        node.CFrame = node.CFrame;
        node.LocalCFrame = node.LocalCFrame;
        scene.StaticWorldDirty.ShouldBeFalse();

        node.CFrame = A;
        scene.StaticWorldDirty.ShouldBeTrue();
    }

    [Fact]
    public void Under_a_scaled_parent_the_position_is_the_world_position_and_the_rotation_leaves_scale_out()
    {
        var (_, parent, child) = ParentAndChild(A);
        parent.LocalScale = new Vector3(2f, 2f, 2f);
        child.LocalCFrame = B;

        // Twice as far out as A * B: (1,2,3) + 2 * (6,5,-4). The same rotation.
        child.CFrame.ShouldHaveComponents(
            13, 12, -5,
            0, 1, 0,
            0, 0, -1,
            -1, 0, 0);
        child.CFrame.Position.ShouldBe(child.WorldPosition);
        child.CFrame.LookVector.ShouldBeNear(Facing(child), Loose);
    }

    [Fact]
    public void Under_a_stretched_parent_the_rotation_is_still_the_rotations_composed()
    {
        var (_, parent, child) = ParentAndChild(new CFrame(1f, 2f, 3f));
        parent.LocalScale = new Vector3(3f, 1f, 1f);
        child.LocalCFrame = new CFrame(4f, 5f, 6f) * CFrame.Angles(0f, 0.6f, 0f);

        // The matrix is sheared here and holds no rotation of its own.
        CFrame.TryFromMatrix(child.WorldMatrix, out _).ShouldBeFalse();
        child.CFrame.Position.ShouldBeNear(new Vector3(13f, 7f, 9f));
        child.CFrame.ShouldBeNear(new CFrame(13f, 7f, 9f) * CFrame.Angles(0f, 0.6f, 0f));
    }

    [Fact]
    public void Setting_the_world_frame_under_a_scaled_parent_lands_the_position_where_asked()
    {
        var (_, parent, child) = ParentAndChild(Slanted);
        parent.LocalScale = new Vector3(3f, 0.5f, 2f);
        CFrame asked = new CFrame(10f, -4f, 2f) * CFrame.Angles(0.2f, 1.3f, -0.4f);

        child.CFrame = asked;

        child.WorldPosition.ShouldBeNear(asked.Position, Loose);
        child.CFrame.ShouldBeNear(asked, Loose);
    }

    [Fact]
    public void Under_a_parent_scaled_to_nothing_a_write_still_gives_finite_numbers()
    {
        var (_, parent, child) = ParentAndChild(A);
        parent.LocalScale = Vector3.Zero;

        child.CFrame = A * B;

        // Solved as if nothing were scaled.
        child.LocalCFrame.ShouldBeNear(B);
    }
}
