using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Core.Hosting;

/// <summary>What happened to one node between two frames.</summary>
public enum SceneChangeKind
{
    /// <summary>The node entered the graph.</summary>
    Added,

    /// <summary>The node left the graph.</summary>
    Removed,

    /// <summary>
    /// The node moved to a different parent, or to a different position under
    /// the same one, without leaving the graph.
    /// </summary>
    Reparented,

    /// <summary>The node's name changed. Carries the new name.</summary>
    Renamed,
}

/// <summary>
/// One structural change to the scene graph, as a value a UI thread can hold:
/// ids and a name, never a <c>SceneNode</c>. Changes are a log; replay them in order.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="NodeId">The node it happened to.</param>
/// <param name="ParentId">
/// The node's parent after the change, or <see cref="Guid.Empty"/> for a node
/// that has none (a removed node, or a scene root).
/// </param>
/// <param name="Name">The node's name at the moment of the change.</param>
/// <param name="SiblingIndex">
/// The node's position among its parent's children after the change, or -1 when
/// it has no parent.
/// </param>
/// <param name="NodeKind">What the node is, derived from its payloads at the moment of the change.</param>
public readonly record struct SceneChange(
    SceneChangeKind Kind,
    Guid NodeId,
    Guid ParentId,
    string Name,
    int SiblingIndex,
    SceneNodeKind NodeKind = SceneNodeKind.Empty);
