using System;
using System.Collections.Generic;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

// Where each light and entity node comes in the scene tree, a parent before
// its children and children in their order. The scene's own lists are in the
// order nodes were attached, which a delete and its undo change.
internal sealed class SceneTreeOrder
{
    // Only filled during a walk, so no node is held between them.
    private readonly Dictionary<SceneNode, int> _walked = new(ReferenceEqualityComparer.Instance);

    private int[] _lights = [];
    private int[] _entities = [];
    private Scene? _scene;
    private bool _isCurrent;

    // Walks the tree again when it changed since the last walk, or a node
    // got or lost its light or entity.
    public void Refresh(Scene scene)
    {
        if (!ReferenceEquals(scene, _scene))
            Watch(scene);

        if (_isCurrent)
            return;

        Walk(scene.Root);
        Fill(scene.LightNodes, ref _lights);
        Fill(scene.EntityNodes, ref _entities);
        _walked.Clear();
        _isCurrent = true;
    }

    // The tree place of the node at this place in Scene.LightNodes.
    public int OfLight(int listed) => _lights[listed];

    // The tree place of the node at this place in Scene.EntityNodes.
    public int OfEntity(int listed) => _entities[listed];

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
        _isCurrent = false;
    }

    private void Forget(SceneNode changed) => _isCurrent = false;

    private void Walk(SceneNode node)
    {
        if (node.Light is not null || node.Entity is not null)
            _walked[node] = _walked.Count;

        IReadOnlyList<SceneNode> children = node.Children;
        for (int i = 0; i < children.Count; i++)
            Walk(children[i]);
    }

    private void Fill(IReadOnlyList<SceneNode> listed, ref int[] places)
    {
        if (places.Length < listed.Count)
            places = new int[Math.Max(listed.Count, places.Length * 2)];

        for (int i = 0; i < listed.Count; i++)
            places[i] = _walked.TryGetValue(listed[i], out int place) ? place : int.MaxValue;
    }
}
