using System.Collections.Generic;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

// Where each light and entity node comes in the scene tree, a parent before
// its children and children in their order. The scene's own lists are in the
// order nodes were attached, which a delete and its undo change.
internal sealed class SceneTreeOrder
{
    private readonly Dictionary<SceneNode, int> _places = new(ReferenceEqualityComparer.Instance);
    private Scene? _scene;
    private bool _isCurrent;

    // The node's place in the tree, among the nodes with a light or an entity.
    public int Of(Scene scene, SceneNode node)
    {
        if (!ReferenceEquals(scene, _scene))
            Watch(scene);

        if (!_isCurrent)
        {
            Walk(scene.Root);
            _isCurrent = true;
        }

        return _places.TryGetValue(node, out int place) ? place : int.MaxValue;
    }

    private void Watch(Scene scene)
    {
        if (_scene is { } watched)
        {
            watched.NodeAdded -= Forget;
            watched.NodeRemoved -= Forget;
            watched.NodeReparented -= Forget;
            watched.NodeKindChanged -= Forget;
        }

        _scene = scene;
        scene.NodeAdded += Forget;
        scene.NodeRemoved += Forget;
        scene.NodeReparented += Forget;
        scene.NodeKindChanged += Forget;
        Forget(scene.Root);
    }

    // Lets go of every node too, so a level that was closed is not kept.
    private void Forget(SceneNode changed)
    {
        _isCurrent = false;
        _places.Clear();
    }

    private void Walk(SceneNode node)
    {
        if (node.Light is not null || node.Entity is not null)
            _places[node] = _places.Count;

        IReadOnlyList<SceneNode> children = node.Children;
        for (int i = 0; i < children.Count; i++)
            Walk(children[i]);
    }
}
