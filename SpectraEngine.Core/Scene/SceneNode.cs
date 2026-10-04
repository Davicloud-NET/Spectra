using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// A node in the scene graph: a name, a local <see cref="Transform"/>,
/// optional payloads, and a parent/child hierarchy. Render thread only.
/// </summary>
public class SceneNode
{
    private readonly List<SceneNode> _children = [];
    private Transform _localTransform = Transform.Identity;
    private Matrix4x4 _worldMatrix = Matrix4x4.Identity;
    private bool _worldDirty = true;
    private Bsp.Brush? _brush;
    private BrushKind _brushKind = BrushKind.World;
    private MeshRenderer? _meshRenderer;
    private Light? _light;
    private PhysicsFlags _physicsFlags = PhysicsFlags.Default;
    private byte _collisionGroup;

    // Brushes in this subtree, this node included. The first counts every
    // kind (rigidity: no scale above any brush), the second only World brushes
    // (dirtying). AdjustSubtreeBrushCounts is the only writer of either.
    private int _subtreeBrushCount;
    private int _subtreeStaticWorldBrushCount;

    public SceneNode(string name = "Node")
    {
        Name = name;
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Creates a node under an existing id. Undo of a delete and map loading
    /// use this, so commands that name the id still resolve.
    /// </summary>
    public SceneNode(string name, Guid id)
    {
        Name = name;
        Id = id;
    }

    /// <summary>
    /// The node's identity, fixed at construction. Saves and edit history name
    /// nodes by this id; <see cref="Scene.TryFindById"/> resolves it.
    /// </summary>
    public Guid Id { get; }
    // Separate from Id: the scene index tolerates duplicate ids, and they
    // must not collapse two placements.
    internal Guid PlacementIdentity { get; } = Guid.NewGuid();

    private string _name = "Node";

    /// <summary>
    /// The node's display name. Renaming an attached node raises
    /// <see cref="Scene.NodeRenamed"/>.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (string.Equals(_name, value, StringComparison.Ordinal))
                return;
            _name = value;
            Owner?.OnNodeRenamed(this);
        }
    }

    public SceneNode? Parent { get; private set; }

    // The scene this node is attached to, or null. A whole subtree always
    // shares one owner.
    internal Scene? Owner { get; private set; }

    public IReadOnlyList<SceneNode> Children => _children;
    internal SceneNode? PreviousSibling { get; private set; }
    private SceneNode? _nextSibling;

    private void UnlinkSiblings()
    {
        if (PreviousSibling is { } previous) previous._nextSibling = _nextSibling;
        if (_nextSibling is { } next) next.PreviousSibling = PreviousSibling;
        PreviousSibling = _nextSibling = null;
    }

    /// <summary>
    /// Renderable geometry attached to this node, if any. Setting it updates
    /// the scene's spatial index.
    /// </summary>
    public MeshRenderer? MeshRenderer
    {
        get => _meshRenderer;
        set
        {
            if (ReferenceEquals(_meshRenderer, value))
                return;
            _meshRenderer = value;
            // Or a save would name a model the node no longer draws.
            if (value is null)
                MeshSource = null;
            Owner?.OnNodeSpatialComponentChanged(this);
        }
    }

    /// <summary>
    /// The model file <see cref="MeshRenderer"/> came from, or null when it was
    /// built in code. Set it after the renderer; clearing the renderer clears it.
    /// </summary>
    public MeshSource? MeshSource { get; set; }

    /// <summary>
    /// The light this node emits, or null. The owning scene tracks lit nodes
    /// in its own list.
    /// </summary>
    // A light does not put the node in the BVH: default physics flags would
    // make every lamp pickable and collidable.
    public Light? Light
    {
        get => _light;
        set
        {
            if (ReferenceEquals(_light, value))
                return;
            _light = value;
            Owner?.UpdateLightMembership(this);
        }
    }

    /// <summary>
    /// Entity data for this node, or null: class name, keyvalues and output
    /// wiring. Names a class, so an unknown class still loads and saves.
    /// </summary>
    public Entities.EntityData? Entity { get; set; }

    /// <summary>
    /// Brush geometry this node contributes, if any. The node's world
    /// transform places it, not <see cref="Bsp.Brush.Transform"/>, so build
    /// the brush with node-local extents and attach an instance to one node only.
    /// </summary>
    public Bsp.Brush? Brush
    {
        get => _brush;
        set
        {
            if (ReferenceEquals(_brush, value))
                return;

            bool had = _brush is not null;
            bool has = value is not null;
            bool world = _brushKind == BrushKind.World;
            _brush = value;

            if (had != has)
                AdjustSubtreeBrushCounts(this, has ? 1 : -1, world ? (has ? 1 : -1) : 0);

            // Attach or detach shifts placement slots, so it needs the full
            // walk; a swap keeps the layout and dirties only this node. A part
            // brush is not in the placement list and must dirty nothing.
            if (world)
            {
                if (had != has)
                    Owner?.MarkStructuralWorldDirty();
                else
                    Owner?.MarkBrushSubtreeDirty(this);
            }

            // Not gated on kind: part brushes are culled and picked through
            // the BVH too.
            Owner?.OnNodeSpatialComponentChanged(this);
        }
    }

    /// <summary>
    /// Whether this node's brush is fused into the compiled static world or
    /// stands alone as a movable part. Not inherited by children.
    /// </summary>
    public BrushKind BrushKind
    {
        get => _brushKind;
        set
        {
            if (_brushKind == value)
                return;

            _brushKind = value;

            if (_brush is null)
                return;

            AdjustSubtreeBrushCounts(this, 0, value == BrushKind.World ? 1 : -1);
            Owner?.MarkAdmissionChanged(this);
        }
    }

    /// <summary>True when this node's brush is part of the static-world compile.</summary>
    public bool IsStaticWorldBrush => _brush is not null && _brushKind == BrushKind.World;

    /// <summary>
    /// The node's physics and query bits; see <see cref="Scene.PhysicsFlags"/>.
    /// Writing them dirties nothing.
    /// </summary>
    public PhysicsFlags PhysicsFlags
    {
        get => _physicsFlags;
        set => _physicsFlags = value;
    }

    /// <summary>Whether this node's geometry participates in collision.</summary>
    public bool CanCollide
    {
        get => (_physicsFlags & PhysicsFlags.CanCollide) != 0;
        set => SetFlag(PhysicsFlags.CanCollide, value);
    }

    /// <summary>
    /// Whether this node's geometry is visible to spatial queries. Independent
    /// of <see cref="CanCollide"/>.
    /// </summary>
    public bool CanQuery
    {
        get => (_physicsFlags & PhysicsFlags.CanQuery) != 0;
        set => SetFlag(PhysicsFlags.CanQuery, value);
    }

    /// <summary>Whether this node generates touch and trigger events.</summary>
    public bool CanTouch
    {
        get => (_physicsFlags & PhysicsFlags.CanTouch) != 0;
        set => SetFlag(PhysicsFlags.CanTouch, value);
    }

    /// <summary>
    /// Whether this node is exempt from simulation. Default <c>true</c>; see
    /// <see cref="PhysicsFlags.Anchored"/>.
    /// </summary>
    public bool Anchored
    {
        get => (_physicsFlags & PhysicsFlags.Anchored) != 0;
        set => SetFlag(PhysicsFlags.Anchored, value);
    }

    /// <summary>
    /// The node's collision group, an id from the scene's
    /// <see cref="Scene.CollisionGroups"/> registry. Zero
    /// (<see cref="Scene.CollisionGroups.DefaultGroup"/>) unless assigned.
    /// </summary>
    // Only range-checked: a node can get its group before it has a scene.
    public int CollisionGroup
    {
        get => _collisionGroup;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, CollisionGroups.MaxGroups);
            _collisionGroup = (byte)value;
        }
    }

    private void SetFlag(PhysicsFlags flag, bool on)
    {
        if (on)
            _physicsFlags |= flag;
        else
            _physicsFlags &= ~flag;
    }

    /// <summary>
    /// The node's transform relative to its parent. Writing an equal value
    /// changes nothing and raises no event.
    /// </summary>
    public Transform LocalTransform
    {
        get => _localTransform;
        set
        {
            if (value.Position == _localTransform.Position &&
                value.Rotation == _localTransform.Rotation &&
                value.Scale == _localTransform.Scale)
                return;
            _localTransform = value;
            OnLocalTransformChanged();
        }
    }

    public Vector3 LocalPosition
    {
        get => _localTransform.Position;
        set
        {
            if (value == _localTransform.Position)
                return;
            _localTransform.Position = value;
            OnLocalTransformChanged();
        }
    }

    public Quaternion LocalRotation
    {
        get => _localTransform.Rotation;
        set
        {
            if (value == _localTransform.Rotation)
                return;
            _localTransform.Rotation = value;
            OnLocalTransformChanged();
        }
    }

    public Vector3 LocalScale
    {
        get => _localTransform.Scale;
        set
        {
            if (value == _localTransform.Scale)
                return;
            _localTransform.Scale = value;
            OnLocalTransformChanged();
        }
    }

    /// <summary>
    /// How many brushes of any kind are in this node's subtree, its own
    /// included. Check this before writing <see cref="LocalScale"/>: a scale
    /// anywhere above a brush makes its placement non-rigid.
    /// </summary>
    public int SubtreeBrushCount => _subtreeBrushCount;

    /// <summary>
    /// How many <see cref="BrushKind.World"/> brushes are in this node's
    /// subtree. Moving the node costs a recompile only when this is non-zero.
    /// </summary>
    public int SubtreeStaticWorldBrushCount => _subtreeStaticWorldBrushCount;

    /// <summary>The node's accumulated world matrix (local composed with all ancestors).</summary>
    public Matrix4x4 WorldMatrix
    {
        get
        {
            if (_worldDirty)
            {
                var local = _localTransform.Model;
                _worldMatrix = Parent is null ? local : local * Parent.WorldMatrix;
                _worldDirty = false;
            }
            return _worldMatrix;
        }
    }

    public Vector3 WorldPosition => WorldMatrix.Translation;

    /// <summary>
    /// This node's position among its parent's children, or -1 with no parent.
    /// Sibling order is carve order, so a structural edit must record it to be
    /// reversible. Linear in the sibling count.
    /// </summary>
    public int IndexInParent => Parent?._children.IndexOf(this) ?? -1;

    /// <summary>
    /// Attaches an existing node as the last child, detaching it from any
    /// previous parent.
    /// </summary>
    public SceneNode AddChild(SceneNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return InsertChild(_children.Count, child);
    }

    /// <summary>
    /// Attaches an existing node as a child at a chosen position, detaching it
    /// from any previous parent. Throws when the node is this node or one of
    /// its ancestors.
    /// </summary>
    /// <param name="index">
    /// Where in the child list the node lands. Clamped to the list's length
    /// after the detach.
    /// </param>
    /// <param name="child">The node to attach.</param>
    public SceneNode InsertChild(int index, SceneNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        // A cycle would hang the next graph walk.
        for (SceneNode? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, child))
            {
                throw new ArgumentException(
                    $"Cannot attach '{child.Name}' under '{Name}': it is that node or an ancestor of it, " +
                    "which would make the graph a cycle.",
                    nameof(child));
            }
        }

        Scene? previousOwner = child.Owner;

        if (child.Parent is { } oldParent)
        {
            child.UnlinkSiblings();
            oldParent._children.Remove(child);
            if (child._subtreeBrushCount > 0)
            {
                AdjustSubtreeBrushCounts(oldParent, -child._subtreeBrushCount, -child._subtreeStaticWorldBrushCount);
                // Only world brushes change the compiled world.
                if (child._subtreeStaticWorldBrushCount > 0)
                    child.Owner?.MarkStructuralWorldDirty();
            }
        }

        // Clamp after the detach: a move within one parent, or an undo into a
        // parent that lost siblings, can name an index past the end.
        if (index > _children.Count)
            index = _children.Count;

        child.Parent = this;
        child.PreviousSibling = index > 0 ? _children[index - 1] : null;
        child._nextSibling = index < _children.Count ? _children[index] : null;
        if (child.PreviousSibling is { } previousSibling) previousSibling._nextSibling = child;
        if (child._nextSibling is { } nextSibling) nextSibling.PreviousSibling = child;
        _children.Insert(index, child);
        // Before SetOwner: NodeAdded handlers read WorldMatrix.
        child.MarkWorldDirty();
        child.SetOwner(Owner);

        if (child._subtreeBrushCount > 0)
        {
            AdjustSubtreeBrushCounts(this, child._subtreeBrushCount, child._subtreeStaticWorldBrushCount);
            if (child._subtreeStaticWorldBrushCount > 0)
                Owner?.MarkStructuralWorldDirty();
        }

        // A reparent inside one scene raises no membership events, but the
        // spatial index still has to refit.
        if (previousOwner is not null && ReferenceEquals(previousOwner, Owner))
            previousOwner.OnNodeSubtreeMoved(child);

        return child;
    }

    /// <summary>Creates a new child node and attaches it.</summary>
    public SceneNode CreateChild(string name = "Node")
    {
        var node = new SceneNode(name);
        return AddChild(node);
    }

    /// <summary>
    /// A detached copy of this node with a new id, for the caller to attach.
    /// The mesh renderer is shared; the brush, light and entity are copied.
    /// </summary>
    /// <param name="deep">
    /// True (the default) to copy the whole subtree; false for this node alone.
    /// </param>
    public SceneNode Clone(bool deep = true)
    {
        var copy = new SceneNode(Name);

        copy._localTransform = _localTransform;
        // HasBody names the original's entry in the physics side table.
        copy._physicsFlags = _physicsFlags & ~PhysicsFlags.HasBody;
        copy._collisionGroup = _collisionGroup;

        // Kind before brush, so the brush setter counts it once.
        copy._brushKind = _brushKind;

        // Through the properties: they maintain the subtree counters.
        copy.MeshRenderer = _meshRenderer;
        // After the renderer: its setter clears the source on null.
        copy.MeshSource = MeshSource;
        // Own brush instance: the carve cache keys on reference identity.
        copy.Brush = _brush?.CloneShape();
        copy.Light = _light?.Clone();
        copy.Entity = Entity?.Clone();

        if (deep)
        {
            for (int i = 0; i < _children.Count; i++)
                copy.AddChild(_children[i].Clone(deep: true));
        }

        return copy;
    }

    public void RemoveChild(SceneNode child)
    {
        if (_children.Remove(child))
        {
            child.UnlinkSiblings();
            child.Parent = null;
            if (child._subtreeBrushCount > 0)
            {
                AdjustSubtreeBrushCounts(this, -child._subtreeBrushCount, -child._subtreeStaticWorldBrushCount);
                if (child._subtreeStaticWorldBrushCount > 0)
                    Owner?.MarkStructuralWorldDirty();
            }
            child.SetOwner(null);
            child.MarkWorldDirty();
        }
    }

    /// <summary>
    /// Enumerates this node and all of its descendants in pre-order, children
    /// in list order.
    /// </summary>
    public IEnumerable<SceneNode> Traverse()
    {
        // Explicit stack: recursive yield allocates an enumerator per node.
        var stack = new Stack<SceneNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            // Reversed so the first child pops first.
            var children = node._children;
            for (int i = children.Count - 1; i >= 0; i--)
                stack.Push(children[i]);
        }
    }

    // Moves the whole subtree to another scene, raising NodeRemoved then
    // NodeAdded per node, parents first. An unchanged owner stops the walk,
    // which is why a reparent inside one scene raises neither.
    internal void SetOwner(Scene? owner)
    {
        if (Owner == owner)
            return;

        Scene? previous = Owner;
        // Set before the events, so handlers see the new membership.
        Owner = owner;
        previous?.OnNodeRemoved(this);
        owner?.OnNodeAdded(this);

        for (int i = 0; i < _children.Count; i++)
            _children[i].SetOwner(owner);
    }

    // The only writer of either count.
    private static void AdjustSubtreeBrushCounts(SceneNode node, int totalDelta, int worldDelta)
    {
        for (SceneNode? n = node; n is not null; n = n.Parent)
        {
            n._subtreeBrushCount += totalDelta;
            n._subtreeStaticWorldBrushCount += worldDelta;
        }
    }

    private void OnLocalTransformChanged()
    {
        MarkWorldDirty();

        // World brushes only: moving a subtree of parts must cost no recompile.
        if (_subtreeStaticWorldBrushCount > 0)
            Owner?.MarkBrushSubtreeDirty(this);

        Owner?.OnNodeTransformChanged(this);
    }

    // Eager, whole subtree. Fine for shallow trees.
    private void MarkWorldDirty()
    {
        _worldDirty = true;
        for (int i = 0; i < _children.Count; i++)
            _children[i].MarkWorldDirty();
    }
}
