using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Draws the distances a selected entity's class declares, each as a sphere
/// round the node: how far a sound is heard, say.
/// </summary>
// Which settings are distances comes from the schema, so a class written
// outside the engine gets its spheres too. The viewport knows no class name.
public sealed class EntityDistanceOverlay
{
    /// <summary>The default colour of an entity's smallest distance, the marker's green.</summary>
    public static readonly Vector3 DefaultColor = EntityMarkerOverlay.DefaultColor;

    /// <summary>The default colour of every larger distance, the same green dimmed.</summary>
    public static readonly Vector3 DefaultOuterColor = EntityMarkerOverlay.DefaultColor * 0.45f;

    /// <summary>Whether the overlay draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The colour of an entity's smallest distance.</summary>
    public Vector3 Color { get; set; } = DefaultColor;

    /// <summary>The colour of every larger distance, so two read as two.</summary>
    public Vector3 OuterColor { get; set; } = DefaultOuterColor;

    /// <summary>
    /// How many spheres may be drawn in one frame. The rest are counted in
    /// <see cref="SkippedLastDraw"/>.
    /// </summary>
    public int MaxSpheres { get; set; } = 64;

    /// <summary>Spheres the last draw drew.</summary>
    public int DrawnLastDraw { get; private set; }

    /// <summary>Spheres the last draw skipped because <see cref="MaxSpheres"/> was reached.</summary>
    public int SkippedLastDraw { get; private set; }

    /// <summary>Draws a sphere per distance of every selected entity whose class declares one.</summary>
    /// <param name="output">The depth-off overlay buffer.</param>
    public void Draw(DebugDraw output, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scene);

        DrawnLastDraw = 0;
        SkippedLastDraw = 0;

        if (!Enabled || scene.EntitySchemas is not { } schemas)
            return;

        IReadOnlyList<SceneNode> selection = scene.Selection.Items;
        for (int i = 0; i < selection.Count; i++)
        {
            SceneNode node = selection[i];
            if (node.Entity is { } entity && schemas.TryGetSchema(entity.ClassName, out EntitySchema? schema))
                DrawDistances(output, node.WorldPosition, entity, schema.Keyvalues);
        }
    }

    private void DrawDistances(
        DebugDraw output, Vector3 at, EntityData entity, IReadOnlyList<KeyvalueDescriptor> declared)
    {
        // Read twice: the smallest has to be known before any is coloured.
        float smallest = float.PositiveInfinity;
        for (int i = 0; i < declared.Count; i++)
        {
            if (TryReadDistance(entity, declared[i], out float radius))
                smallest = MathF.Min(smallest, radius);
        }

        for (int i = 0; i < declared.Count; i++)
        {
            if (!TryReadDistance(entity, declared[i], out float radius))
                continue;

            if (DrawnLastDraw >= MaxSpheres)
            {
                SkippedLastDraw++;
                continue;
            }

            WireRing.Sphere(output, at, radius, radius > smallest ? OuterColor : Color);
            DrawnLastDraw++;
        }
    }

    // The reader a class's own keyvalues go through, so a value is the same
    // number here as in the level.
    private static bool TryReadDistance(EntityData entity, in KeyvalueDescriptor declared, out float radius)
    {
        radius = 0f;
        if (declared.Type != KeyvalueType.Distance)
            return false;

        string text = entity.TryGetValue(declared.Name, out string authored) ? authored : declared.Default;
        return KeyvalueWire.TryParseFloat(text, out radius) && radius > 0f;
    }
}
