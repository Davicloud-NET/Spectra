using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Editing.Commands;

/// <summary>One value a property panel is asking to write.</summary>
public readonly record struct PropertyEdit
{
    public required PropertyId Id { get; init; }

    /// <summary>
    /// Tells apart rows that share an id: the keyvalue's key for
    /// <see cref="PropertyId.EntityKeyvalue"/>, the plane index for face
    /// properties. Empty otherwise.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Which components of <see cref="Vector"/> to write. Unmasked components
    /// keep each node's own value, so a bulk edit of y leaves x and z alone.
    /// </summary>
    public PropertyAxes Axes { get; init; } = PropertyAxes.All;

    public string Text { get; init; } = string.Empty;
    public float Number { get; init; }
    public Vector3 Vector { get; init; }
    public bool Flag { get; init; }

    public PropertyEdit() { }
}

/// <summary>
/// Applies a property-panel edit to a selection as one history entry. Nodes
/// that already hold the value record nothing. Render thread only.
/// </summary>
public static class PropertyEditor
{
    /// <summary>
    /// Writes <paramref name="edit"/> to every node in
    /// <paramref name="targets"/> that carries the property.
    /// </summary>
    /// <param name="inGesture">
    /// True while the caller holds a transaction open around a drag, so this
    /// edit joins it instead of becoming its own history entry.
    /// </param>
    /// <returns>How many nodes changed.</returns>
    public static int Apply(
        UndoStack undo, IReadOnlyList<SceneNode> targets, PropertyEdit edit, bool inGesture = false)
    {
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count == 0)
            return 0;

        // Needed for keyvalue defaults: an unauthored key shows its default,
        // and committing that must not write it into the map.
        EntitySchemaCatalog? schemas = undo.Scene.EntitySchemas;

        var commands = new List<IEditorCommand>();
        foreach (SceneNode node in targets)
        {
            if (node is null) continue;
            if (Build(node, edit, schemas) is { } command)
                commands.Add(command);
        }

        if (commands.Count == 0)
            return 0;

        // Inside a gesture the caller owns the transaction; a second Begin throws.
        bool ownTransaction = !inGesture;
        if (ownTransaction)
            undo.BeginTransaction(NameOf(edit.Id));

        foreach (IEditorCommand command in commands)
            undo.Execute(command);

        if (ownTransaction)
            undo.CommitTransaction();

        return commands.Count;
    }

    // Null when the node does not carry the property or already holds the value.
    private static IEditorCommand? Build(
        SceneNode node, PropertyEdit edit, EntitySchemaCatalog? schemas) => edit.Id switch
    {
        PropertyId.NodeName => BuildName(node, edit),
        PropertyId.Position or PropertyId.Rotation or PropertyId.Scale => BuildTransform(node, edit),
        PropertyId.BrushKind => BuildBrushKind(node, edit),
        PropertyId.BrushOperation => BuildBrushOperation(node, edit),
        PropertyId.BrushSize => BuildBrushSize(node, edit),
        PropertyId.BrushMaterial => BuildBrushMaterial(node, edit),
        PropertyId.FaceMaterial or PropertyId.FaceAlignment
            or PropertyId.FaceUScale or PropertyId.FaceVScale
            or PropertyId.FaceUOffset or PropertyId.FaceVOffset
            or PropertyId.FaceRotation => BuildFace(node, edit),
        PropertyId.LightKind or PropertyId.LightColor or PropertyId.LightIntensity
            or PropertyId.LightRange or PropertyId.LightEnabled
            or PropertyId.LightInnerAngle or PropertyId.LightOuterAngle
            or PropertyId.LightWidth or PropertyId.LightHeight
            or PropertyId.LightRadius => BuildLight(node, edit),

        PropertyId.CanCollide or PropertyId.CanQuery or PropertyId.CanTouch
            or PropertyId.IsRendered => BuildNodeFlags(node, edit),

        PropertyId.EntityKeyvalue => BuildEntityKeyvalue(node, edit, schemas),

        // Read-only properties are ignored, not thrown on.
        _ => null,
    };

    private static IEditorCommand? BuildName(SceneNode node, PropertyEdit edit)
    {
        string name = edit.Text.Trim();

        if (name.Length == 0 || string.Equals(node.Name, name, StringComparison.Ordinal))
            return null;

        return SetNodeNameCommand.Capture(node, name);
    }

    private static IEditorCommand? BuildTransform(SceneNode node, PropertyEdit edit)
    {
        Transform current = node.LocalTransform;
        Transform next = current;

        switch (edit.Id)
        {
            case PropertyId.Position:
                next.Position = Merge(current.Position, edit.Vector, edit.Axes);
                break;

            case PropertyId.Scale:
                // A brush placement must stay rigid. A scaled brush node makes
                // the placement snapshot defective and the static world stops
                // recompiling.
                if (node.Brush is not null)
                    return null;

                next.Scale = Merge(current.Scale, edit.Vector, edit.Axes);
                break;

            case PropertyId.Rotation:
                // Merged in degrees: a quaternion has no per-axis components
                // to leave alone.
                Vector3 currentDegrees = EulerAngles.FromQuaternion(current.Rotation).AsDegrees;
                Vector3 merged = Merge(currentDegrees, edit.Vector, edit.Axes);
                next.Rotation = EulerAngles.FromDegrees(merged).ToQuaternion();
                break;
        }

        // Exact equality, same as the transform setters' early-out.
        if (next.Position == current.Position
            && next.Rotation == current.Rotation
            && next.Scale == current.Scale)
        {
            return null;
        }

        return SetLocalTransformCommand.Capture(node, next);
    }

    private static IEditorCommand? BuildBrushKind(SceneNode node, PropertyEdit edit)
    {
        if (node.Brush is null) return null;

        BrushKind kind = string.Equals(edit.Text, "Part", StringComparison.OrdinalIgnoreCase)
            ? BrushKind.Part
            : BrushKind.World;

        if (node.BrushKind == kind) return null;
        if (kind == BrushKind.World) return SetBrushKindCommand.Capture(node, kind);

        // To a part, the face axes are baked so the texture stays put.
        var commands = new List<IEditorCommand>(2);
        BrushKindConversion.AppendToPart(node, commands);
        return commands.Count == 1 ? commands[0] : new CompositeCommand(NameOf(PropertyId.BrushKind), commands);
    }

    private static IEditorCommand? BuildBrushOperation(SceneNode node, PropertyEdit edit)
    {
        if (node.Brush is not { } brush) return null;

        BrushOperation operation =
            string.Equals(edit.Text, "Subtractive", StringComparison.OrdinalIgnoreCase)
                ? BrushOperation.Subtractive
                : BrushOperation.Additive;

        if (brush.Operation == operation) return null;

        return SetBrushCommand.Capture(node, brush.WithOperation(operation));
    }

    private static IEditorCommand? BuildBrushSize(SceneNode node, PropertyEdit edit)
    {
        if (node.Brush is not { } brush) return null;

        Aabb bounds = brush.LocalBounds;
        Vector3 current = bounds.Max - bounds.Min;
        Vector3 target = Merge(current, edit.Vector, edit.Axes);

        // The edit is a world size; the factor is derived per node.
        if (!IsUsable(target.X) || !IsUsable(target.Y) || !IsUsable(target.Z))
            return null;

        if (current.X <= 0f || current.Y <= 0f || current.Z <= 0f)
            return null;

        var factor = new Vector3(target.X / current.X, target.Y / current.Y, target.Z / current.Z);
        if (factor == Vector3.One) return null;

        return SetBrushCommand.Capture(node, brush.WithScaledExtents(factor));
    }

    // Must match NodeInspector.KindLabel.
    private static LightKind ParseKind(string? text) => text switch
    {
        "Point" => LightKind.Point,
        "Spot" => LightKind.Spot,
        "Rect" => LightKind.Rect,
        "Disc" => LightKind.Disc,
        _ => LightKind.Directional,
    };

    private static IEditorCommand? BuildLight(SceneNode node, PropertyEdit edit)
    {
        if (node.Light is not { } light) return null;

        SetLightCommand.Settings current = SetLightCommand.Settings.From(light);
        SetLightCommand.Settings next = edit.Id switch
        {
            PropertyId.LightKind => current with { Kind = ParseKind(edit.Text) },
            PropertyId.LightColor => current with { Color = Merge(current.Color, edit.Vector, edit.Axes) },
            PropertyId.LightIntensity => current with { Intensity = edit.Number },
            PropertyId.LightRange => current with { Range = edit.Number },
            PropertyId.LightEnabled => current with { Enabled = edit.Flag },

            // Angles and extents are clamped by Light's setters, not refused.
            PropertyId.LightInnerAngle => current with { InnerAngle = edit.Number },
            PropertyId.LightOuterAngle => current with { OuterAngle = edit.Number },
            PropertyId.LightWidth => current with { Width = edit.Number },
            PropertyId.LightHeight => current with { Height = edit.Number },
            PropertyId.LightRadius => current with { Radius = edit.Number },

            _ => current,
        };

        // Light's intensity and range setters throw. Refuse here, before a
        // command can throw from Do inside an open transaction.
        if (PropertyLimits.Refusal(PropertyId.LightIntensity, next.Intensity) is not null) return null;
        if (PropertyLimits.Refusal(PropertyId.LightRange, next.Range) is not null) return null;
        if (!float.IsFinite(next.Color.X) || !float.IsFinite(next.Color.Y) || !float.IsFinite(next.Color.Z))
            return null;

        return next == current ? null : SetLightCommand.Capture(node, next);
    }

    private static IEditorCommand? BuildNodeFlags(SceneNode node, PropertyEdit edit)
    {
        // Only where the inspector offers the row. A bulk edit must not clear
        // Drawn on a world brush, where nothing shows it until the brush
        // becomes a part.
        bool offered = edit.Id == PropertyId.IsRendered
            ? NodeInspector.HasDrawnRow(node)
            : NodeInspector.HasFlagRows(node);

        if (!offered) return null;

        SetNodeFlagsCommand.NodeFlags current = SetNodeFlagsCommand.NodeFlags.From(node);
        SetNodeFlagsCommand.NodeFlags next = edit.Id switch
        {
            PropertyId.CanCollide => current.With(PhysicsFlags.CanCollide, edit.Flag),
            PropertyId.CanQuery => current.With(PhysicsFlags.CanQuery, edit.Flag),
            PropertyId.CanTouch => current.With(PhysicsFlags.CanTouch, edit.Flag),
            PropertyId.IsRendered => current with { IsRendered = edit.Flag },
            _ => current,
        };

        return next == current ? null : SetNodeFlagsCommand.Capture(node, next);
    }

    // Keyvalues are stored as wire text, so the edit is text too.
    private static IEditorCommand? BuildEntityKeyvalue(
        SceneNode node, PropertyEdit edit, EntitySchemaCatalog? schemas)
    {
        if (node.Entity is not { } entity) return null;

        if (string.IsNullOrEmpty(edit.Key)) return null;

        // What the panel shows: the stored text, or the schema default for an
        // unauthored key.
        string effective = entity.TryGetValue(edit.Key, out string stored)
            ? stored
            : DefaultFor(schemas, entity.ClassName, edit.Key);

        string next = edit.Axes == PropertyAxes.All
            ? edit.Text
            : MergeWireAxes(effective, edit.Text, edit.Axes);

        // Also keeps an absent key's default out of the map file.
        if (string.Equals(next, effective, StringComparison.Ordinal))
            return null;

        return SetEntityKeyvalueCommand.Capture(node, edit.Key, next);
    }

    // The class's declared default for the key, or empty.
    private static string DefaultFor(EntitySchemaCatalog? schemas, string className, string key)
    {
        if (schemas is null || !schemas.TryGetSchema(className, out EntitySchema? schema))
            return "";

        IReadOnlyList<KeyvalueDescriptor> declared = schema.Keyvalues;
        for (int i = 0; i < declared.Count; i++)
        {
            if (string.Equals(declared[i].Name, key, StringComparison.Ordinal))
                return declared[i].Default;
        }

        return "";
    }

    // Splices the masked components of edited into current at the token level.
    // Parsing and reformatting would rewrite the author's spelling of the
    // untouched components ("1.0", "1e0") and dirty the map file.
    // A value that is not three tokens is written whole.
    private static string MergeWireAxes(string current, string edited, PropertyAxes axes)
    {
        Span<int> currentStart = stackalloc int[3];
        Span<int> currentEnd = stackalloc int[3];
        Span<int> editedStart = stackalloc int[3];
        Span<int> editedEnd = stackalloc int[3];

        if (!TryFindComponents(current, currentStart, currentEnd)
            || !TryFindComponents(edited, editedStart, editedEnd))
        {
            return edited;
        }

        var merged = new StringBuilder(current.Length + edited.Length);
        int copied = 0;
        for (int i = 0; i < 3; i++)
        {
            if (!axes.HasFlag(AxisAt(i)))
                continue;

            merged.Append(current, copied, currentStart[i] - copied);
            merged.Append(edited, editedStart[i], editedEnd[i] - editedStart[i]);
            copied = currentEnd[i];
        }

        merged.Append(current, copied, current.Length - copied);
        return merged.ToString();
    }

    // True only for three whitespace-separated tokens, like KeyvalueWire.
    private static bool TryFindComponents(string text, Span<int> starts, Span<int> ends)
    {
        int found = 0;
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= text.Length)
                break;

            if (found == 3)
                return false;

            starts[found] = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;
            ends[found] = i;
            found++;
        }

        return found == 3;
    }

    private static PropertyAxes AxisAt(int index) => index switch
    {
        0 => PropertyAxes.X,
        1 => PropertyAxes.Y,
        _ => PropertyAxes.Z,
    };

    private static Vector3 Merge(Vector3 current, Vector3 edited, PropertyAxes axes) => new(
        axes.HasFlag(PropertyAxes.X) ? edited.X : current.X,
        axes.HasFlag(PropertyAxes.Y) ? edited.Y : current.Y,
        axes.HasFlag(PropertyAxes.Z) ? edited.Z : current.Z);

    private static bool IsUsable(float value) => float.IsFinite(value) && value > 0f;

    // Paints every face. An empty path means the default material.
    private static IEditorCommand? BuildBrushMaterial(SceneNode node, PropertyEdit edit)
    {
        if (node.Brush is not { } brush) return null;
        if (!TryResolveMaterial(edit.Text, out MaterialRef material)) return null;

        Brush next = brush.WithAllFacesMaterial(material);

        return ReferenceEquals(next, brush) ? null : SetBrushCommand.Capture(node, next);
    }

    // Edits one face; edit.Key is the plane index. The edit is in world space
    // and the surface is stored brush-local, so it maps in and back out.
    private static IEditorCommand? BuildFace(SceneNode node, PropertyEdit edit)
    {
        if (node.Brush is not { } brush) return null;

        if (!int.TryParse(edit.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int planeIndex))
            return null;

        if (planeIndex < 0 || planeIndex >= brush.FaceSurfaces.Count) return null;

        FaceSurface local = brush.FaceSurfaces[planeIndex];

        if (edit.Id == PropertyId.FaceMaterial)
        {
            if (!TryResolveMaterial(edit.Text, out MaterialRef material)) return null;

            FaceSurface painted = local.WithMaterial(material);
            return painted.Equals(local) ? null : SetBrushCommand.Capture(
                node, brush.WithFaceSurface(planeIndex, painted));
        }

        Matrix4x4 world = node.WorldMatrix;
        if (!Matrix4x4.Invert(world, out Matrix4x4 inverse)) return null;

        Vector3 normal = FaceAxes.WorldNormal(brush.LocalPlanes[planeIndex], world);
        FaceSurface current = local.Transformed(world);
        FaceSurface edited;

        switch (edit.Id)
        {
            case PropertyId.FaceAlignment:
                edited = string.Equals(edit.Text, "Face", StringComparison.Ordinal)
                    ? FaceAxes.AlignedToFace(in current, normal)
                    : FaceAxes.AlignedToWorld(in current);
                break;

            case PropertyId.FaceRotation:
                if (!float.IsFinite(edit.Number)) return null;
                edited = FaceAxes.WithRotation(in current, normal, edit.Number);
                break;

            // FaceSurface's constructor throws on a zero scale, so refuse it first.
            case PropertyId.FaceUScale:
                if (!IsUsableScale(edit.Number)) return null;
                edited = current.WithAxes(
                    current.UAxis, current.VAxis, current.UOffset, current.VOffset,
                    edit.Number, current.VScale);
                break;

            case PropertyId.FaceVScale:
                if (!IsUsableScale(edit.Number)) return null;
                edited = current.WithAxes(
                    current.UAxis, current.VAxis, current.UOffset, current.VOffset,
                    current.UScale, edit.Number);
                break;

            case PropertyId.FaceUOffset:
                if (!float.IsFinite(edit.Number)) return null;
                edited = current.WithAxes(
                    current.UAxis, current.VAxis, edit.Number, current.VOffset,
                    current.UScale, current.VScale);
                break;

            case PropertyId.FaceVOffset:
                if (!float.IsFinite(edit.Number)) return null;
                edited = current.WithAxes(
                    current.UAxis, current.VAxis, current.UOffset, edit.Number,
                    current.UScale, current.VScale);
                break;

            default:
                return null;
        }

        FaceSurface back = edited.Transformed(inverse);
        return back.Equals(local) ? null : SetBrushCommand.Capture(
            node, brush.WithFaceSurface(planeIndex, back));
    }

    private static bool IsUsableScale(float value) => float.IsFinite(value) && value > 0f;

    // False for a path that cannot be normalised. The registry interns whatever
    // it is given, so a bad path would become an unresolvable reference.
    private static bool TryResolveMaterial(string? path, out MaterialRef material)
    {
        material = MaterialRef.Default;

        if (string.IsNullOrWhiteSpace(path)) return true;

        try
        {
            material = MaterialRegistry.Intern(ContentRoot.NormalizeRelativePath(path));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string NameOf(PropertyId id) => id switch
    {
        PropertyId.NodeName => "Rename",
        PropertyId.Position => "Move",
        PropertyId.Rotation => "Rotate",
        PropertyId.Scale => "Scale",
        PropertyId.BrushKind => "Convert Brush",
        PropertyId.BrushOperation => "Brush Operation",
        PropertyId.BrushSize => "Resize",
        PropertyId.BrushMaterial => "Material",
        PropertyId.FaceMaterial => "Face Material",
        PropertyId.FaceAlignment or PropertyId.FaceUScale or PropertyId.FaceVScale
            or PropertyId.FaceUOffset or PropertyId.FaceVOffset
            or PropertyId.FaceRotation => "Face Texture",
        PropertyId.EntityKeyvalue => "Entity Property",
        PropertyId.CanCollide or PropertyId.CanQuery or PropertyId.CanTouch
            or PropertyId.IsRendered => "Behavior",
        _ => "Light",
    };
}
