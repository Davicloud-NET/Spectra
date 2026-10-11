using System.Text;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// A level as a save writes it and as the door reads it. Taken before Play
// and after Stop, the two must be the same.
internal sealed class LevelState
{
    private readonly byte[] _saved;
    private readonly string _members;

    public LevelState(Scene scene)
    {
        _saved = MapWriter.Write(MapSceneBinder.FromScene(scene));
        _members = Members(scene);
    }

    // Whether both save to the same bytes.
    public bool IsSavedAs(LevelState other) => _saved.AsSpan().SequenceEqual(other._saved);

    public void ShouldBe(LevelState before, string run)
    {
        // Line by line first: a failure then says where the two part.
        FirstDifference(Encoding.UTF8.GetString(before._saved), Encoding.UTF8.GetString(_saved))
            .ShouldBeNull($"{run}: the level saves to other bytes after Stop than before Play");
        _saved.ShouldBe(before._saved, run);

        FirstDifference(before._members, _members)
            .ShouldBeNull($"{run}: a member a script can set reads another value after Stop than before Play");
    }

    // Every member a script can set, of every node and the root, as the door
    // reads it back. The map holds less: nothing of the root is saved.
    private static string Members(Scene scene)
    {
        var dump = new StringBuilder();
        foreach (SceneNode node in scene.Root.Traverse())
        {
            foreach (SettableMember member in ScriptWrites.Settable)
            {
                string value = ScriptWrites.Read(node, member.Id).ToString();
                dump.Append(node.Id).Append(' ').Append(member.Name).Append(" = ").Append(value).Append('\n');
            }
        }

        return dump.ToString();
    }

    // The first line that is not the same in both, or null.
    private static string? FirstDifference(string before, string after)
    {
        string[] was = before.Split('\n');
        string[] now = after.Split('\n');
        for (int i = 0; i < Math.Max(was.Length, now.Length); i++)
        {
            string old = i < was.Length ? was[i].Trim() : "(no line)";
            string current = i < now.Length ? now[i].Trim() : "(no line)";
            if (old != current)
                return $"line {i + 1} was <{old}> and is <{current}>";
        }

        return null;
    }
}
