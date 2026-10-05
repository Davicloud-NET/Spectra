using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Commands;

/// <summary>Turns a block into a part without moving its texture.</summary>
// The carve maps UVs over world-space vertices and a part's mesh over
// brush-local ones. A world-aligned face has no stored axes to carry across,
// so unbaked it jumps by the node's transform.
public static class BrushKindConversion
{
    /// <summary>
    /// The brush with every world-aligned face given the axes the carve derives
    /// for it under <paramref name="world"/>, stored in brush space. The same
    /// instance comes back when there is nothing to bake.
    /// </summary>
    // The result is no longer world-aligned, and converting back does not
    // restore that. Undo does, since it puts the old brush back.
    public static Brush BakeFaceAxes(Brush brush, in Matrix4x4 world)
    {
        ArgumentNullException.ThrowIfNull(brush);

        // A cut draws nothing as a part, so its faces stay as authored.
        if (brush.Operation == BrushOperation.Subtractive)
            return brush;

        if (!Matrix4x4.Invert(world, out Matrix4x4 toLocal))
            return brush;

        IReadOnlyList<FaceSurface> faces = brush.FaceSurfaces;
        FaceSurface[]? baked = null;

        for (int i = 0; i < faces.Count; i++)
        {
            FaceSurface face = faces[i];
            if (!face.IsWorldAligned)
                continue;

            // The same normal the carve resolves against, so a face near a
            // dominant-axis tie picks the same axes here.
            Vector3 normal = Plane.Transform(brush.LocalPlanes[i], world).Normal;
            face.ResolveAxes(normal, out Vector3 u, out Vector3 v, out float uScale, out float vScale);

            baked ??= [.. faces];
            baked[i] = face
                .WithAxes(u, v, face.UOffset, face.VOffset, uScale, vScale)
                .Transformed(toLocal);
        }

        return baked is null
            ? brush
            : new Brush(brush.LocalPlanes, brush.Transform, baked, brush.Operation);
    }

    /// <summary>
    /// Adds the commands that make <paramref name="node"/>'s block a part.
    /// Adds nothing when it has no brush or is a part already.
    /// </summary>
    /// <returns>Whether anything was added.</returns>
    public static bool AppendToPart(SceneNode node, List<IEditorCommand> into)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(into);

        if (node.Brush is not { } brush || node.BrushKind == BrushKind.Part)
            return false;

        // Kind first: the baked brush then never enters the carve.
        into.Add(SetBrushKindCommand.Capture(node, BrushKind.Part));

        Brush baked = BakeFaceAxes(brush, node.WorldMatrix);
        if (!ReferenceEquals(baked, brush))
            into.Add(new SetBrushCommand(node.Id, brush, baked));

        return true;
    }
}
