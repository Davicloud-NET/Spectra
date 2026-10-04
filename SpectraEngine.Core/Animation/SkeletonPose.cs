using System;
using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Animation;

/// <summary>
/// One instance's posed skeleton: local transforms, the model-space matrices
/// they compose to, and the skinning matrices a vertex shader wants. Nothing
/// allocates after construction. Posing is safe on any thread as long as each
/// pose has one writer.
/// </summary>
public sealed class SkeletonPose
{
    private readonly Transform[] _local;
    private readonly Matrix4x4[] _model;
    private readonly Matrix4x4[] _skinning;

    public SkeletonPose(Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        Skeleton = skeleton;
        _local = new Transform[skeleton.BoneCount];
        _model = new Matrix4x4[skeleton.BoneCount];
        _skinning = new Matrix4x4[skeleton.BoneCount];

        ResetToBind();
        BuildMatrices();
    }

    public Skeleton Skeleton { get; }

    public int BoneCount => _local.Length;

    /// <summary>
    /// Per-bone transforms in parent space. Animation and blending write here. To
    /// override a joint, write it and call <see cref="BuildMatrices"/>.
    /// </summary>
    public Span<Transform> Local => _local;

    /// <summary>
    /// Per-bone bone-space to model-space matrices, for sockets and attachments.
    /// Valid after <see cref="BuildMatrices"/>.
    /// </summary>
    public ReadOnlySpan<Matrix4x4> Model => _model;

    /// <summary>Per-bone mesh-space to posed-model-space matrices, for a skinning shader.</summary>
    public ReadOnlySpan<Matrix4x4> Skinning => _skinning;

    /// <summary>Puts every bone back on its rest pose.</summary>
    public void ResetToBind() => Skeleton.CopyBindPose(_local);

    /// <summary>
    /// Writes the clip's pose at <paramref name="time"/> into <see cref="Local"/>.
    /// Bones the clip has no channel for go back to bind. Does not build matrices.
    /// </summary>
    public void Sample(AnimationClip clip, float time)
    {
        ArgumentNullException.ThrowIfNull(clip);

        ResetToBind();

        float t = clip.NormalizeTime(time);
        ReadOnlySpan<AnimationChannel> channels = clip.Channels;
        ReadOnlySpan<SkeletonBone> bones = Skeleton.Bones;

        for (int i = 0; i < channels.Length; i++)
        {
            AnimationChannel channel = channels[i];
            int bone = channel.BoneIndex;

            // The clip may have been authored against a different skeleton.
            if ((uint)bone >= (uint)_local.Length)
                continue;

            Transform bind = bones[bone].LocalBind;
            _local[bone] = channel.SampleAt(t, in bind);
        }
    }

    /// <summary>
    /// Blends the local transforms of two poses into a third: <paramref name="weight"/>
    /// 0 is all <paramref name="from"/>, 1 is all <paramref name="to"/>. The
    /// destination may alias either source.
    /// </summary>
    // Blends components, not matrices: lerping matrices shears the mesh.
    public static void Blend(SkeletonPose from, SkeletonPose to, float weight, SkeletonPose destination)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(destination);

        if (!ReferenceEquals(from.Skeleton, to.Skeleton) || !ReferenceEquals(from.Skeleton, destination.Skeleton))
            throw new ArgumentException("All three poses must share one skeleton.", nameof(destination));

        float w = Math.Clamp(weight, 0f, 1f);

        ReadOnlySpan<Transform> a = from._local;
        ReadOnlySpan<Transform> b = to._local;
        Span<Transform> result = destination._local;

        for (int i = 0; i < result.Length; i++)
        {
            Quaternion qa = a[i].Rotation;
            Quaternion qb = b[i].Rotation;

            // Shortest path, as in AnimationChannel's rotation sampling.
            if (Quaternion.Dot(qa, qb) < 0f)
                qb = -qb;

            result[i] = new Transform
            {
                Position = Vector3.Lerp(a[i].Position, b[i].Position, w),
                Rotation = Quaternion.Normalize(Quaternion.Slerp(qa, qb, w)),
                Scale = Vector3.Lerp(a[i].Scale, b[i].Scale, w),
            };
        }
    }

    /// <summary>
    /// Composes <see cref="Local"/> into <see cref="Model"/> and <see cref="Skinning"/>.
    /// </summary>
    public void BuildMatrices()
    {
        ReadOnlySpan<SkeletonBone> bones = Skeleton.Bones;

        // One forward pass: parents come before children. Row-vector convention,
        // so local * parentModel and inverseBind * boneModel.
        for (int i = 0; i < bones.Length; i++)
        {
            Matrix4x4 local = _local[i].Model;
            int parent = bones[i].ParentIndex;

            _model[i] = parent < 0 ? local : local * _model[parent];
            _skinning[i] = bones[i].InverseBindPose * _model[i];
        }
    }

    /// <summary>
    /// The model-space matrix of a named bone. Per-frame callers should resolve
    /// the index once and read <see cref="Model"/>.
    /// </summary>
    public bool TryGetBoneMatrix(string name, out Matrix4x4 matrix)
    {
        if (Skeleton.TryGetBoneIndex(name, out int index))
        {
            matrix = _model[index];
            return true;
        }

        matrix = Matrix4x4.Identity;
        return false;
    }
}
