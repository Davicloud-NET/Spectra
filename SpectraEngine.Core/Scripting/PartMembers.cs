using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Scripting;

// The switches a script reads and sets on a part or a mesh, for
// ScriptWrites. The mover, the touch tracker and every query read a
// node's switches when they run, so a change counts from the next of them.
internal static class PartMembers
{
    // None for a node that is neither a part nor a mesh.
    public static MemberValue Read(SceneNode node, SettableMemberId member)
    {
        if (NodeClass.KindOf(node) is not (NodeClassKind.Part or NodeClassKind.MeshPart))
            return MemberValue.None;

        return member switch
        {
            SettableMemberId.CanCollide => MemberValue.Of(node.CanCollide),
            SettableMemberId.CanQuery => MemberValue.Of(node.CanQuery),
            SettableMemberId.CanTouch => MemberValue.Of(node.CanTouch),
            SettableMemberId.Visible => MemberValue.Of(node.IsRendered),
            _ => MemberValue.None,
        };
    }

    // The node is a part or a mesh.
    public static bool TryWrite(
        PlayJournal journal, SceneNode node, in SettableMember member, bool value,
        [NotNullWhen(false)] out string? refusal)
    {
        // The level's fused world was built with what the brush said then,
        // and under a baked map the node has no brush left to switch.
        if (IsWorldBrush(node))
        {
            refusal =
                $"{node.Name} cannot change its {member.Name}: it is a world brush, fused into the level, " +
                "and only parts can change while a level plays. Convert the brush to a part.";
            return false;
        }

        refusal = null;
        JournalKind<bool> kind = KindOf(member.Id);
        if (kind.Read(node) == value)
            return true;

        journal.Record(node, kind);
        switch (member.Id)
        {
            case SettableMemberId.CanCollide:
                node.CanCollide = value;
                break;
            case SettableMemberId.CanQuery:
                node.CanQuery = value;
                break;
            case SettableMemberId.CanTouch:
                node.CanTouch = value;
                break;
            default:
                node.IsRendered = value;
                break;
        }

        return true;
    }

    // The node's own brush. A part above a world brush keeps its switches:
    // none of them is inherited.
    private static bool IsWorldBrush(SceneNode node) =>
        node.IsStaticWorldBrush || node.BakedKind is SceneNodeKind.BrushWorld or SceneNodeKind.BrushSubtractive;

    private static JournalKind<bool> KindOf(SettableMemberId member) => member switch
    {
        SettableMemberId.CanCollide => FlagJournalKind.CanCollide,
        SettableMemberId.CanQuery => FlagJournalKind.CanQuery,
        SettableMemberId.CanTouch => FlagJournalKind.CanTouch,
        _ => FlagJournalKind.IsRendered,
    };
}
