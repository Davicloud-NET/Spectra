using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System.Runtime.CompilerServices;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Puts the tags and attributes a level was built with into the tables of a
// play session: for every node when the scripts start, and for a node that
// comes into the level later. A node is read once. Scripts then change the
// tables' copy and nothing writes to a node, so Stop has nothing to put back.
internal sealed class LevelSeed(LuauHost host, EntityWorld world, TagTable tags, AttributeTable attributes)
{
    private static readonly object WasRead = new();

    // The nodes that were read. Only one with a tag or an attribute is
    // noted, and weakly: once the tables let a node go, this does not hold it.
    private readonly ConditionalWeakTable<SceneNode, object> _read = new();

    // Every node of the level. No signal fires: no script has run yet.
    public void ReadLevel()
    {
        foreach (SceneNode node in world.Scene.Root.Traverse())
            Read(node, isInLevel: true);
    }

    // What one node was built with, unless it was read before. A node that
    // is just coming into the level is not in its tags' lists yet: whoever
    // tells the tables of its coming puts it there, and fires the signals.
    public void Read(SceneNode node, bool isInLevel)
    {
        TagSet built = node.Tags;
        AttributeSet set = node.Attributes;
        if ((built.Count == 0 && set.Count == 0) || !_read.TryAdd(node, WasRead))
            return;

        for (int i = 0; i < built.Count; i++)
            tags.Add(node, built[i], isInLevel, isBuilt: true);

        for (int i = 0; i < set.Count; i++)
        {
            string name = set[i].Key;

            // A keyvalue of the node's entity has the name already, and the
            // entity runs with that. A second value under it would be one
            // scripts read and the entity does not.
            if (AuthoredAttributes.TryRead(node, world.Catalog, name, out _, out _))
                Warn(node, name);
            else
                attributes.SetBuilt(node, attributes.NameOf(name), set[i].Value);
        }
    }

    // A copy a script made has what scripts left on its source, and the
    // tables have that already. Read, it would get its source's built
    // values back.
    public void Copied(SceneNode copy)
    {
        if (copy.Tags.Count > 0 || copy.Attributes.Count > 0)
            _read.TryAdd(copy, WasRead);
    }

    public void Clear() => _read.Clear();

    private void Warn(SceneNode node, string name) =>
        host.Report(new ScriptMessage(
            ScriptSeverity.Warning,
            $"'{node.Name}' has an attribute called '{name}', and its entity has a setting of that name. " +
            "Scripts read the entity's setting. Give the attribute another name.",
            string.Empty,
            0));
}
