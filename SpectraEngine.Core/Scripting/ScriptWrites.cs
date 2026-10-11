using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// The one way a script changes a node while a level plays. A write records
/// the value the node had in the level's journal first, so stopping the
/// level puts it back. Also the list of what a script can set on a node, and
/// a read of each.
/// </summary>
public sealed class ScriptWrites
{
    // In the order of SettableMemberId.
    private static readonly SettableMember[] Members =
    [
        new(SettableMemberId.Position, "Position", MemberValueKind.Vector3),
        new(SettableMemberId.CFrame, "CFrame", MemberValueKind.CFrame),
        new(SettableMemberId.LocalPosition, "LocalPosition", MemberValueKind.Vector3),
        new(SettableMemberId.LocalCFrame, "LocalCFrame", MemberValueKind.CFrame),
        new(SettableMemberId.Name, "Name", MemberValueKind.Text),
    ];

    private static readonly byte[][] Utf8Names =
        Array.ConvertAll(Members, static member => Encoding.UTF8.GetBytes(member.Name));

    private readonly EntityWorld _world;

    /// <summary>The writes of the scripts of one level.</summary>
    public ScriptWrites(EntityWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <summary>Every member of a node a script can set.</summary>
    public static IReadOnlyList<SettableMember> Settable => Members;

    /// <summary>Finds a member a script can set by its name in UTF-8, which is how Luau hands a name over.</summary>
    public static bool TryFind(ReadOnlySpan<byte> name, out SettableMember member)
    {
        for (int i = 0; i < Utf8Names.Length; i++)
        {
            if (name.SequenceEqual(Utf8Names[i]))
            {
                member = Members[i];
                return true;
            }
        }

        member = default;
        return false;
    }

    /// <summary>The error for a value of the wrong kind, in Roblox's words.</summary>
    /// <param name="got">What a script calls the kind of value it handed over.</param>
    public static string WrongKind(in SettableMember member, string got) =>
        $"Unable to assign property {member.Name}. {MemberValue.TypeName(member.Kind)} expected, got {got}";

    /// <summary>What a member reads as on a node now.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The id names no member.</exception>
    public static MemberValue Read(SceneNode node, SettableMemberId member)
    {
        ArgumentNullException.ThrowIfNull(node);

        return member switch
        {
            SettableMemberId.Position => MemberValue.Of(node.WorldPosition),
            SettableMemberId.CFrame => MemberValue.Of(node.CFrame),
            SettableMemberId.LocalPosition => MemberValue.Of(node.LocalPosition),
            SettableMemberId.LocalCFrame => MemberValue.Of(node.LocalCFrame),
            SettableMemberId.Name => MemberValue.Of(node.Name),
            _ => throw NoSuchMember(member),
        };
    }

    /// <summary>
    /// Sets a member of a node. The value the node had is kept the first
    /// time, and <see cref="EntityWorld.Deactivate"/> puts it back.
    /// </summary>
    /// <param name="refusal">Why nothing was written, as the script's error says it.</param>
    /// <returns>False when the write was refused. The node is as it was then.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The id names no member.</exception>
    public bool TryWrite(
        SceneNode node, SettableMemberId member, in MemberValue value, [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(node);

        SettableMember described = Describe(member);
        if (value.Kind != described.Kind)
        {
            refusal = WrongKind(described, MemberValue.TypeName(value.Kind));
            return false;
        }

        if (!_world.IsRunningEntities)
        {
            refusal = $"{node.Name} cannot be changed: the level has stopped.";
            return false;
        }

        return member switch
        {
            SettableMemberId.Position =>
                TryMove(node, LocalAt(node, new CFrame(value.Vector, node.CFrame.Rotation)), out refusal),
            SettableMemberId.CFrame => TryMove(node, LocalAt(node, value.Frame), out refusal),
            SettableMemberId.LocalPosition =>
                TryMove(node, node.LocalTransform with { Position = value.Vector }, out refusal),
            SettableMemberId.LocalCFrame => TryMove(node, LocalUnderParent(node, value.Frame), out refusal),
            _ => TryRename(node, value.Text, out refusal),
        };
    }

    private static SettableMember Describe(SettableMemberId member) =>
        (uint)member < (uint)Members.Length ? Members[(int)member] : throw NoSuchMember(member);

    private static ArgumentOutOfRangeException NoSuchMember(SettableMemberId member) =>
        new(nameof(member), member, "This is no member a script can set.");

    // A frame the node already has is left alone. Solved again it would come
    // back a rounding error away, and a write of what was read would move it.
    private static Transform LocalAt(SceneNode node, CFrame world) =>
        world == node.CFrame ? node.LocalTransform : node.LocalTransformAt(world);

    private static Transform LocalUnderParent(SceneNode node, CFrame local) =>
        local == node.LocalCFrame
            ? node.LocalTransform
            : node.LocalTransform with { Position = local.Position, Rotation = local.Rotation };

    private bool TryMove(SceneNode node, in Transform local, [NotNullWhen(false)] out string? refusal)
    {
        // A node at such a place is in no cell of the scene's index, and no
        // query finds it again. A rotation is brought to unit length when a
        // CFrame is made, so only the position can be off.
        Vector3 position = local.Position;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
        {
            refusal = $"{node.Name} cannot be moved: a position must be three numbers, with none of them infinite.";
            return false;
        }

        // The entity world records the move, as it does for an entity.
        if (_world.TrySetLocalTransform(node, in local, out string? why))
        {
            refusal = null;
            return true;
        }

        refusal = $"{node.Name} cannot be moved: {why}.";
        return false;
    }

    private bool TryRename(SceneNode node, string name, [NotNullWhen(false)] out string? refusal)
    {
        if (NodeName.WhyRefused(name) is { } why)
        {
            refusal = $"{node.Name} cannot be renamed: {why}.";
            return false;
        }

        // The index of names hears the rename, so a wire finds the entity
        // by its new name from the next event on.
        _world.Journal.Record(node, JournalKind.Name);
        node.Name = name;
        refusal = null;
        return true;
    }
}
