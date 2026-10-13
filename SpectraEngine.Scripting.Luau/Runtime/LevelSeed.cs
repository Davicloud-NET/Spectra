using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Puts the tags and attributes a level was built with into the tables of a
// play session: for every node when the scripts start, and for a node that
// comes into the level later. A node is read once. From then on the tables
// hold a copy that scripts change, and nothing here writes to a node, so
// Stop leaves every node as it was built.
internal sealed class LevelSeed(LuauHost host, EntityWorld world, TagTable tags, AttributeTable attributes)
{
    // The nodes that were read. Only one with a tag or an attribute is kept.
    private readonly HashSet<SceneNode> _read = [];

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
        if ((built.Count == 0 && set.Count == 0) || !_read.Add(node))
            return;

        for (int i = 0; i < built.Count; i++)
            tags.Add(node, built[i], isInLevel);

        for (int i = 0; i < set.Count; i++)
        {
            string name = set[i].Key;

            // A keyvalue of the node's entity has the name already, and the
            // entity runs with that. A second value under it would be one
            // scripts read and the entity does not.
            if (AuthoredAttributes.TryRead(node, world.Catalog, name, out _, out _))
                Warn(node, name);
            else
                attributes.Set(node, attributes.NameOf(name), set[i].Value);
        }
    }

    // For whoever takes a node out of the tables for good.
    public void Forget(SceneNode node) => _read.Remove(node);

    public void Clear() => _read.Clear();

    private void Warn(SceneNode node, string name) =>
        host.Report(new ScriptMessage(
            ScriptSeverity.Warning,
            $"'{node.Name}' has an attribute called '{name}', and its entity has a setting of that name. " +
            "Scripts read the entity's setting. Give the attribute another name.",
            string.Empty,
            0));
}
