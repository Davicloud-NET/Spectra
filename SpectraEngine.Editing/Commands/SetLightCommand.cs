using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Writes a node's light settings. The caller must pass a valid range:
/// <c>Light.Range</c> throws on zero or less and this does not clamp.
/// </summary>
// Light is mutable, so capture values, not the instance: a held reference
// would already carry the redo's fields. Edited in place so the scene's
// light list is not re-registered per edit.
public sealed class SetLightCommand : ICoalescingCommand
{
    /// <summary>A light's settings, as a value.</summary>
    public readonly record struct Settings(
        LightKind Kind, Vector3 Color, float Intensity, float Range, bool Enabled,
        float InnerAngle, float OuterAngle, float Width, float Height, float Radius)
    {
        /// <summary>Reads the current settings off a light.</summary>
        public static Settings From(Light light)
        {
            ArgumentNullException.ThrowIfNull(light);
            return new Settings(
                light.Kind, light.Color, light.Intensity, light.Range, light.Enabled,
                light.InnerAngle, light.OuterAngle, light.Width, light.Height, light.Radius);
        }

        /// <summary>Writes these settings onto a light.</summary>
        public void ApplyTo(Light light)
        {
            ArgumentNullException.ThrowIfNull(light);
            light.Kind = Kind;
            light.Color = Color;
            light.Intensity = Intensity;
            light.Range = Range;
            light.Enabled = Enabled;
            // Inner before outer: OuterAngle clamps against the inner one.
            light.InnerAngle = InnerAngle;
            light.OuterAngle = OuterAngle;
            light.Width = Width;
            light.Height = Height;
            light.Radius = Radius;
        }
    }

    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after settings.</summary>
    public SetLightCommand(Guid nodeId, Settings before, Settings after)
    {
        NodeId = nodeId;
        Before = before;
        After = after;
    }

    /// <summary>
    /// Captures the node's current light settings as the before state. Call
    /// before applying the edit. Throws when the node carries no light.
    /// </summary>
    public static SetLightCommand Capture(SceneNode node, Settings after)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Light is not { } light)
        {
            throw new InvalidOperationException(
                $"Node '{node.Name}' carries no light to edit.");
        }

        return new SetLightCommand(node.Id, Settings.From(light), after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The settings the light carried before the edit.</summary>
    public Settings Before { get; }

    /// <summary>The settings the light carries after the edit.</summary>
    public Settings After { get; private set; }

    /// <summary>Retargets the after state, keeping the captured before state.</summary>
    public void SetAfter(Settings after) => After = after;

    /// <inheritdoc/>
    public bool TryAbsorb(IEditorCommand newer)
    {
        if (newer is not SetLightCommand next || next.NodeId != NodeId)
            return false;

        SetAfter(next.After);
        return true;
    }

    /// <inheritdoc/>
    public string Name { get; init; } = "Light";

    /// <inheritdoc/>
    public void Do(Scene scene) => Apply(scene, After);

    /// <inheritdoc/>
    public void Undo(Scene scene) => Apply(scene, Before);

    /// <inheritdoc/>
    public void RollBack(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.TryFindById(NodeId, out SceneNode? node) && node.Light is { } live)
        {
            Before.ApplyTo(live);
            return;
        }

        if (_lastApplied is not null
            && _lastApplied.TryGetTarget(out SceneNode? detached)
            && detached.Light is { } orphan)
        {
            Before.ApplyTo(orphan);
        }
    }

    private void Apply(Scene scene, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (!scene.TryFindById(NodeId, out SceneNode? node) || node.Light is not { } light)
            return;

        _lastApplied ??= new WeakReference<SceneNode>(node);
        settings.ApplyTo(light);
    }
}
