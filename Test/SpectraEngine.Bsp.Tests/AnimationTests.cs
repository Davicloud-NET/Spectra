using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Animation;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// Row-vector convention: a hierarchy composes as local * parentModel and a
// vertex skins as v * inverseBind * boneModel.
public sealed class AnimationTests
{
    [Fact]
    public void A_bone_whose_parent_comes_after_it_is_refused()
    {
        var bones = new List<SkeletonBone>
        {
            Bone("child", parent: 1),
            Bone("root", parent: -1),
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => new Skeleton(bones));
        Assert.Contains("topological", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_bones_with_one_name_are_refused()
    {
        // Clips bind to bones by name.
        var bones = new List<SkeletonBone>
        {
            Bone("root", parent: -1),
            Bone("hand", parent: 0),
            Bone("hand", parent: 0),
        };

        Assert.Throws<ArgumentException>(() => new Skeleton(bones));
    }

    [Fact]
    public void At_the_bind_pose_every_skinning_matrix_cancels_to_the_identity()
    {
        // Cannot see the multiplication order: A*inv(A) and inv(A)*A are both
        // identity. The posed test below pins the order.
        Skeleton skeleton = Chain();
        var pose = new SkeletonPose(skeleton);

        for (int i = 0; i < pose.BoneCount; i++)
            AssertMatrixClose(Matrix4x4.Identity, pose.Skinning[i], $"bone {i}");
    }

    [Fact]
    public void A_skinning_matrix_carries_a_bind_pose_vertex_to_where_its_bone_moved()
    {
        // Pins the skinning multiplication order: a vertex at a bone's rest
        // position must land where the bone ended up.
        Skeleton skeleton = Chain();
        var pose = new SkeletonPose(skeleton);

        Assert.True(skeleton.TryGetBoneIndex("hand", out int hand));
        Vector3 restPoint = pose.Model[hand].Translation;

        pose.Local[0] = pose.Local[0] with
        {
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
        };
        pose.BuildMatrices();

        Vector3 skinned = Vector3.Transform(restPoint, pose.Skinning[hand]);
        Vector3 expected = pose.Model[hand].Translation;

        Assert.True(Vector3.Distance(skinned, expected) < 1e-3f,
            $"a rest-pose vertex on the hand skinned to {skinned}, but the hand moved to {expected}");

        // Guard: the bone has to have moved, or the check above compares rest to rest.
        Assert.True(Vector3.Distance(expected, restPoint) > 1f,
            "the pose did not actually move the hand, so this test proved nothing");
    }

    [Fact]
    public void A_childs_model_matrix_is_its_local_composed_with_its_parents()
    {
        Skeleton skeleton = Chain();
        var pose = new SkeletonPose(skeleton);

        pose.Local[0] = pose.Local[0] with
        {
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
        };
        pose.BuildMatrices();

        Matrix4x4 expected = pose.Local[1].Model * pose.Model[0];
        AssertMatrixClose(expected, pose.Model[1], "child model");

        // Quarter turn about Y: the child's +x offset now points along -z.
        Vector3 childOrigin = pose.Model[1].Translation;
        Assert.True(childOrigin.Z < -0.5f,
            $"the child should have swung to -z with its parent, but sits at {childOrigin}");
    }

    [Fact]
    public void A_bone_the_clip_does_not_animate_keeps_its_bind_pose()
    {
        Skeleton skeleton = Chain();
        var pose = new SkeletonPose(skeleton);

        var clip = new AnimationClip("root-only", 1f,
        [
            new AnimationChannel(0, positions: [new Vector3Key(0f, new Vector3(5f, 0f, 0f))]),
        ]);

        pose.Sample(clip, 0.5f);

        Assert.Equal(new Vector3(5f, 0f, 0f), pose.Local[0].Position);
        Assert.Equal(skeleton.Bones[1].LocalBind.Position, pose.Local[1].Position);
    }

    [Fact]
    public void Playing_a_clip_after_another_gives_the_same_pose_as_playing_it_cold()
    {
        Skeleton skeleton = Chain();

        var first = new AnimationClip("first", 1f,
        [
            new AnimationChannel(1, positions: [new Vector3Key(0f, new Vector3(0f, 9f, 0f))]),
        ]);
        var second = new AnimationClip("second", 1f,
        [
            new AnimationChannel(0, positions: [new Vector3Key(0f, new Vector3(3f, 0f, 0f))]),
        ]);

        var sequential = new SkeletonPose(skeleton);
        sequential.Sample(first, 0.2f);
        sequential.Sample(second, 0.2f);

        var cold = new SkeletonPose(skeleton);
        cold.Sample(second, 0.2f);

        for (int i = 0; i < skeleton.BoneCount; i++)
            Assert.Equal(cold.Local[i].Position, sequential.Local[i].Position);
    }

    [Fact]
    public void A_position_track_interpolates_between_its_keys()
    {
        Skeleton skeleton = Chain();
        var pose = new SkeletonPose(skeleton);

        var clip = new AnimationClip("slide", 2f,
        [
            new AnimationChannel(0, positions:
            [
                new Vector3Key(0f, Vector3.Zero),
                new Vector3Key(2f, new Vector3(10f, 0f, 0f)),
            ]),
        ]);

        pose.Sample(clip, 0.5f);
        Assert.Equal(2.5f, pose.Local[0].Position.X, 4);

        pose.Sample(clip, 1.5f);
        Assert.Equal(7.5f, pose.Local[0].Position.X, 4);
    }

    [Fact]
    public void A_single_key_track_is_constant()
    {
        Skeleton skeleton = Chain();
        var pose = new SkeletonPose(skeleton);

        var clip = new AnimationClip("held", 3f,
        [
            new AnimationChannel(0, positions: [new Vector3Key(1f, new Vector3(4f, 0f, 0f))]),
        ]);

        foreach (float t in new[] { 0f, 1f, 2.9f })
        {
            pose.Sample(clip, t);
            Assert.Equal(4f, pose.Local[0].Position.X, 4);
        }
    }

    [Fact]
    public void Coincident_keyframe_times_do_not_produce_a_broken_pose()
    {
        // Authoring tools emit these; (time - start) / (end - start) divides by zero.
        Skeleton skeleton = Chain();
        var pose = new SkeletonPose(skeleton);

        var clip = new AnimationClip("degenerate", 1f,
        [
            new AnimationChannel(0, positions:
            [
                new Vector3Key(0.5f, Vector3.Zero),
                new Vector3Key(0.5f, new Vector3(1f, 0f, 0f)),
            ]),
        ]);

        pose.Sample(clip, 0.5f);
        Assert.True(float.IsFinite(pose.Local[0].Position.X), "a coincident key pair produced a NaN pose");
    }

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(2.5f, 0.5f)]
    [InlineData(-0.5f, 1.5f)]   // negative wrap
    public void A_looping_clip_wraps_its_play_head(float input, float expected)
    {
        var clip = new AnimationClip("loop", 2f, [], looping: true);
        Assert.Equal(expected, clip.NormalizeTime(input), 4);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(5f, 2f)]
    public void A_one_shot_clip_clamps_its_play_head(float input, float expected)
    {
        var clip = new AnimationClip("once", 2f, [], looping: false);
        Assert.Equal(expected, clip.NormalizeTime(input), 4);
    }

    [Fact]
    public void Interpolating_two_keys_on_opposite_hemispheres_takes_the_short_way()
    {
        // An exporter may emit q or -q for the same orientation.
        // Passes without our own hemisphere fix too, since System.Numerics.Slerp
        // also does it. It guards against a swap to lerp-and-normalise.
        Skeleton skeleton = Chain();
        var pose = new SkeletonPose(skeleton);

        Quaternion start = Quaternion.Identity;
        Quaternion end = -Quaternion.CreateFromAxisAngle(Vector3.UnitY, 20f * MathF.PI / 180f);

        var clip = new AnimationClip("turn", 1f,
        [
            new AnimationChannel(0, rotations: [new QuaternionKey(0f, start), new QuaternionKey(1f, end)]),
        ]);

        pose.Sample(clip, 0.5f);

        float degrees = AngleBetweenDegrees(Quaternion.Identity, pose.Local[0].Rotation);
        Assert.True(degrees < 15f,
            $"the midpoint should be about 10 degrees from the start, but it is {degrees:0.0} — " +
            "the interpolation went the long way round");
    }

    [Fact]
    public void Blending_two_poses_on_opposite_hemispheres_takes_the_short_way()
    {
        Skeleton skeleton = Chain();
        var from = new SkeletonPose(skeleton);
        var to = new SkeletonPose(skeleton);
        var result = new SkeletonPose(skeleton);

        from.Local[0] = from.Local[0] with { Rotation = Quaternion.Identity };
        to.Local[0] = to.Local[0] with
        {
            Rotation = -Quaternion.CreateFromAxisAngle(Vector3.UnitY, 20f * MathF.PI / 180f),
        };

        SkeletonPose.Blend(from, to, 0.5f, result);

        float degrees = AngleBetweenDegrees(Quaternion.Identity, result.Local[0].Rotation);
        Assert.True(degrees < 15f,
            $"a crossfade midpoint should be about 10 degrees from the start, but it is {degrees:0.0}");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Blending_at_the_extremes_reproduces_a_source_exactly(float weight)
    {
        Skeleton skeleton = Chain();
        var from = new SkeletonPose(skeleton);
        var to = new SkeletonPose(skeleton);
        var result = new SkeletonPose(skeleton);

        from.Local[1] = from.Local[1] with { Position = new Vector3(1f, 2f, 3f) };
        to.Local[1] = to.Local[1] with { Position = new Vector3(-4f, 0f, 8f) };

        SkeletonPose.Blend(from, to, weight, result);

        Vector3 expected = weight == 0f ? from.Local[1].Position : to.Local[1].Position;
        Assert.Equal(expected, result.Local[1].Position);
    }

    [Fact]
    public void A_blend_may_write_into_one_of_its_own_sources()
    {
        // A crossfade does this every frame.
        Skeleton skeleton = Chain();
        var live = new SkeletonPose(skeleton);
        var target = new SkeletonPose(skeleton);

        live.Local[0] = live.Local[0] with { Position = Vector3.Zero };
        target.Local[0] = target.Local[0] with { Position = new Vector3(10f, 0f, 0f) };

        SkeletonPose.Blend(live, target, 0.25f, live);

        Assert.Equal(2.5f, live.Local[0].Position.X, 4);
    }

    [Fact]
    public void Blending_poses_from_different_skeletons_is_refused()
    {
        var a = new SkeletonPose(Chain());
        var b = new SkeletonPose(Chain());
        Assert.Throws<ArgumentException>(() => SkeletonPose.Blend(a, b, 0.5f, a));
    }

    [Fact]
    public void A_named_bones_model_matrix_is_reachable_for_attachments()
    {
        Skeleton skeleton = Chain();
        var pose = new SkeletonPose(skeleton);

        Assert.True(pose.TryGetBoneMatrix("hand", out Matrix4x4 hand));
        Assert.Equal(3f, hand.Translation.X, 4);   // 1 + 1 + 1 up the chain
        Assert.False(pose.TryGetBoneMatrix("tail", out _));
    }

    private static SkeletonBone Bone(string name, int parent) => new()
    {
        Name = name,
        ParentIndex = parent,
        LocalBind = Transform.Identity,
        InverseBindPose = Matrix4x4.Identity,
    };

    // Three bones, each one unit along +x from its parent. Inverse bind poses
    // are derived by composing and inverting, not typed in.
    private static Skeleton Chain()
    {
        (string Name, int Parent, Vector3 Offset)[] layout =
        [
            ("root", -1, Vector3.Zero),
            ("arm", 0, new Vector3(1f, 0f, 0f)),
            ("hand", 1, new Vector3(1f, 0f, 0f)),
        ];

        // Bone 0 sits one unit out too, so the tip lands at x = 3.
        var bones = new List<SkeletonBone>(layout.Length);
        var models = new Matrix4x4[layout.Length];

        for (int i = 0; i < layout.Length; i++)
        {
            (string name, int parent, Vector3 offset) = layout[i];
            Vector3 position = i == 0 ? new Vector3(1f, 0f, 0f) : offset;

            var local = new Transform { Position = position, Rotation = Quaternion.Identity, Scale = Vector3.One };
            models[i] = parent < 0 ? local.Model : local.Model * models[parent];

            Matrix4x4.Invert(models[i], out Matrix4x4 inverseBind);

            bones.Add(new SkeletonBone
            {
                Name = name,
                ParentIndex = parent,
                LocalBind = local,
                InverseBindPose = inverseBind,
            });
        }

        return new Skeleton(bones);
    }

    private static float AngleBetweenDegrees(Quaternion a, Quaternion b)
    {
        float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
        return 2f * MathF.Acos(Math.Clamp(dot, 0f, 1f)) * 180f / MathF.PI;
    }

    private static void AssertMatrixClose(Matrix4x4 expected, Matrix4x4 actual, string what)
    {
        const float Tolerance = 1e-4f;
        Assert.True(
            MathF.Abs(expected.M11 - actual.M11) < Tolerance && MathF.Abs(expected.M12 - actual.M12) < Tolerance &&
            MathF.Abs(expected.M13 - actual.M13) < Tolerance && MathF.Abs(expected.M14 - actual.M14) < Tolerance &&
            MathF.Abs(expected.M21 - actual.M21) < Tolerance && MathF.Abs(expected.M22 - actual.M22) < Tolerance &&
            MathF.Abs(expected.M23 - actual.M23) < Tolerance && MathF.Abs(expected.M24 - actual.M24) < Tolerance &&
            MathF.Abs(expected.M31 - actual.M31) < Tolerance && MathF.Abs(expected.M32 - actual.M32) < Tolerance &&
            MathF.Abs(expected.M33 - actual.M33) < Tolerance && MathF.Abs(expected.M34 - actual.M34) < Tolerance &&
            MathF.Abs(expected.M41 - actual.M41) < Tolerance && MathF.Abs(expected.M42 - actual.M42) < Tolerance &&
            MathF.Abs(expected.M43 - actual.M43) < Tolerance && MathF.Abs(expected.M44 - actual.M44) < Tolerance,
            $"{what}:\nexpected {expected}\nactual   {actual}");
    }
}
