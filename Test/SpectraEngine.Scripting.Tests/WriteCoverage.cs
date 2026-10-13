using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// What the writes of a random run reached while the level played: which
// members took a write, and which members of which nodes read another value
// for it. A run that reached less than all of them proves less than it says.
internal sealed class WriteCoverage
{
    private readonly SceneNode[] _nodes;
    private readonly MemberValue[][] _atStart;
    private readonly int[] _taken = new int[ScriptWrites.Settable.Count];
    private readonly HashSet<(SceneNode Node, SettableMemberId Member)> _changed = [];

    // Made before Play, so the start is the level as it was authored.
    public WriteCoverage(Scene scene)
    {
        _nodes = [.. scene.Root.Traverse()];
        _atStart = [.. _nodes.Select(node => ScriptWrites.Settable.Select(member => Read(node, member)).ToArray())];
    }

    // Makes one write through the door, and notes what it did to the node.
    public bool Write(ScriptWrites door, SceneNode node, SettableMember member, in MemberValue value)
    {
        MemberValue was = Read(node, member);
        if (!door.TryWrite(node, member.Id, in value, out _))
            return false;

        _taken[(int)member.Id]++;
        if (Read(node, member) != was)
            _changed.Add((node, member.Id));

        return true;
    }

    // Writes that scripts made and counted themselves.
    public void Took(SettableMember member, int count) => _taken[(int)member.Id] += count;

    // Notes every member that reads another value now than at the start. For
    // writes made by scripts, which cannot be watched one at a time.
    public void Look()
    {
        for (int i = 0; i < _nodes.Length; i++)
        {
            foreach (SettableMember member in ScriptWrites.Settable)
            {
                if (Read(_nodes[i], member) != _atStart[i][(int)member.Id])
                    _changed.Add((_nodes[i], member.Id));
            }
        }
    }

    public void ShouldBeWhole(string run)
    {
        foreach (SettableMember member in ScriptWrites.Settable)
        {
            _taken[(int)member.Id].ShouldBeGreaterThan(0, $"{run}: no write of {member.Name} was taken");

            for (int i = 0; i < _nodes.Length; i++)
            {
                if (Takes(_nodes[i], member))
                {
                    _changed.Contains((_nodes[i], member.Id)).ShouldBeTrue(
                        $"{run}: no write changed {member.Name} of {_atStart[i][(int)SettableMemberId.Name]}");
                }
            }
        }
    }

    // A level moves no node with a world brush at or below it, and switches
    // nothing on a world brush. A name every node takes, and a light every
    // member a light has. A node gets another parent when it can move.
    private static bool Takes(SceneNode node, SettableMember member) => member.Id switch
    {
        SettableMemberId.Name => true,
        SettableMemberId.Parent => !node.HoldsWorldBrush,
        SettableMemberId.Position or SettableMemberId.CFrame or SettableMemberId.LocalPosition
            or SettableMemberId.LocalCFrame => node.SubtreeStaticWorldBrushCount == 0,
        SettableMemberId.CanCollide or SettableMemberId.CanQuery or SettableMemberId.CanTouch
            or SettableMemberId.Visible => ScriptWrites.Has(node, member.Id) && !node.IsStaticWorldBrush,
        _ => ScriptWrites.Has(node, member.Id),
    };

    private static MemberValue Read(SceneNode node, SettableMember member) => ScriptWrites.Read(node, member.Id);
}
