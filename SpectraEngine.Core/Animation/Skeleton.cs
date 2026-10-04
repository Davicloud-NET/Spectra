using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Animation;

/// <summary>One joint of a <see cref="Skeleton"/>.</summary>
public readonly struct SkeletonBone
{
    /// <summary>The authored joint name. Importers and clips address the bone by it.</summary>
    public required string Name { get; init; }

    /// <summary>Index of the parent bone, or -1 for a root. Always less than this bone's own index.</summary>
    public required int ParentIndex { get; init; }

    /// <summary>The rest pose, in parent-bone space.</summary>
    public required Transform LocalBind { get; init; }

    /// <summary>
    /// Mesh space to this bone's space, at the bind pose. Assimp calls it the
    /// offset matrix.
    /// </summary>
    // Stored, not derived: inverting a bind pose with non-uniform scale is lossy.
    public required Matrix4x4 InverseBindPose { get; init; }
}

/// <summary>
/// A joint hierarchy, flattened into one array with every parent before its
/// children. Immutable: one skeleton backs every instance of a character.
/// </summary>
public sealed class Skeleton
{
    private readonly SkeletonBone[] _bones;
    private readonly Dictionary<string, int> _byName;

    /// <summary>Builds a skeleton from bones already in topological order.</summary>
    /// <exception cref="ArgumentException">
    /// The list is empty, a parent index is out of range or not less than its
    /// child's, or two bones share a name.
    /// </exception>
    public Skeleton(IReadOnlyList<SkeletonBone> bones)
    {
        ArgumentNullException.ThrowIfNull(bones);
        if (bones.Count == 0)
            throw new ArgumentException("A skeleton needs at least one bone.", nameof(bones));

        _bones = new SkeletonBone[bones.Count];
        _byName = new Dictionary<string, int>(bones.Count, StringComparer.Ordinal);

        for (int i = 0; i < bones.Count; i++)
        {
            SkeletonBone bone = bones[i];

            if (bone.ParentIndex >= i)
            {
                throw new ArgumentException(
                    $"Bone {i} ('{bone.Name}') names parent {bone.ParentIndex}, which is not before it. " +
                    "Bones must be in topological order — see the type remarks.",
                    nameof(bones));
            }

            if (bone.ParentIndex < -1)
            {
                throw new ArgumentException(
                    $"Bone {i} ('{bone.Name}') has parent index {bone.ParentIndex}; use −1 for a root.",
                    nameof(bones));
            }

            if (string.IsNullOrEmpty(bone.Name))
                throw new ArgumentException($"Bone {i} has no name; clips address bones by name.", nameof(bones));

            if (!_byName.TryAdd(bone.Name, i))
            {
                throw new ArgumentException(
                    $"Two bones are named '{bone.Name}' (indices {_byName[bone.Name]} and {i}); " +
                    "clips address bones by name, so names must be unique.",
                    nameof(bones));
            }

            _bones[i] = bone;
        }
    }

    public int BoneCount => _bones.Length;

    public ReadOnlySpan<SkeletonBone> Bones => _bones;

    /// <summary>Resolves an authored joint name to its bone index.</summary>
    public bool TryGetBoneIndex(string name, out int index) => _byName.TryGetValue(name, out index);

    /// <summary>Copies the rest pose into <paramref name="destination"/>.</summary>
    public void CopyBindPose(Span<Transform> destination)
    {
        if (destination.Length < _bones.Length)
            throw new ArgumentException($"Need room for {_bones.Length} bones.", nameof(destination));

        for (int i = 0; i < _bones.Length; i++)
            destination[i] = _bones[i].LocalBind;
    }
}
