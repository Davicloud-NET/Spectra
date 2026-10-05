using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// Describes a scene node as a list of editable rows, grouped by the payload
/// each value came from. Render thread only: it reads live nodes.
/// </summary>
public static class NodeInspector
{
    public const string NodeGroup = "Node";
    public const string TransformGroup = "Transform";
    public const string BrushGroup = "Brush";

    /// <summary>The material every face of a brush wears.</summary>
    public const string MaterialGroup = "Material";

    /// <summary>The picked face: its material and its texture frame.</summary>
    public const string FaceGroup = "Face";
    public const string LightGroup = "Light";
    public const string MeshGroup = "Mesh";

    /// <summary>How the node's geometry takes part in play: solid, sensed, drawn.</summary>
    public const string BehaviorGroup = "Behavior";
    public const string EntityGroup = "Entity";

    // Choice tokens per descriptor choice list. Weak, keyed on the schema's own
    // list, so an entry dies with its schema and nothing allocates per publish.
    private static readonly ConditionalWeakTable<object, string[]> ChoiceTokenCache = new();

    // Display words. The tokens below are what the map format and commands use.
    private static readonly string[] BrushKindLabels = ["Block", "Part"];
    private static readonly string[] BrushOperationLabels = ["Adds solid", "Cuts solid"];

    private const string BrushKindHelp =
        "Block: fused into the level and carved by cuts. Part: moves freely and never fuses.";

    private const string BrushOperationHelp =
        "Adds solid: makes geometry. Cuts solid: carves a hole out of the blocks it overlaps; " +
        "a cut on a part does nothing at all.";

    private static readonly string[] BrushKindChoices = ["World", "Part"];
    private static readonly string[] BrushOperationChoices = ["Additive", "Subtractive"];
    private static readonly string[] LightKindChoices =
        ["Directional", "Point", "Spot", "Rect", "Disc"];

    /// <summary>
    /// Fills <paramref name="into"/> with the node's rows, in group order. The list is cleared first.
    /// </summary>
    /// <param name="schemas">
    /// Entity class schemas, or null. Without one an entity's keyvalues still show, as text.
    /// </param>
    public static void Describe(
        SceneNode node, List<PropertyRow> into, EntitySchemaCatalog? schemas = null,
        int pickedPlane = -1)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        into.Add(PropertyRow.OfText(NodeGroup, "Name", PropertyId.NodeName, node.Name));
        into.Add(PropertyRow.ReadOnly(NodeGroup, "Id", PropertyId.NodeId, node.Id.ToString("D")));

        Transform local = node.LocalTransform;
        into.Add(PropertyRow.OfVector(TransformGroup, "Position", PropertyId.Position, local.Position, "su"));
        into.Add(PropertyRow.OfVector(
            TransformGroup, "Rotation", PropertyId.Rotation,
            EulerAngles.FromQuaternion(local.Rotation).AsDegrees, "deg"));
        // No scale row on a brush node: placements must stay rigid or the
        // static world stops compiling. Size is edited through the Brush section.
        if (node.Brush is null)
            into.Add(PropertyRow.OfVector(TransformGroup, "Scale", PropertyId.Scale, local.Scale));

        if (node.Brush is { } brush)
            DescribeBrush(node, brush, into);

        if (node.Light is { } light)
            DescribeLight(light, into);

        if (node.MeshSource is { } mesh)
        {
            into.Add(PropertyRow.ReadOnly(MeshGroup, "Model", PropertyId.MeshModel, mesh.ModelPath));
            into.Add(PropertyRow.ReadOnly(
                MeshGroup, "Submesh", PropertyId.MeshSubmesh,
                mesh.MeshIndex.ToString(CultureInfo.InvariantCulture)));
        }
        else if (node.MeshRenderer is not null)
        {
            // Names no file, so the node will not survive a save.
            into.Add(PropertyRow.ReadOnly(MeshGroup, "Model", PropertyId.MeshModel, "(built in code)"));
        }

        if (HasFlagRows(node))
            DescribeFlags(node, into);

        if (node.Entity is { } entity)
            DescribeEntity(entity, schemas, into);
        // Last, to match PropertyId order, which is what a merged selection uses.
        if (node.Brush is { } surfaced)
            DescribeMaterial(node, surfaced, pickedPlane, into);
    }

    /// <summary>
    /// Fills <paramref name="into"/> with the merged rows for a selection: the union of the
    /// nodes' properties in <see cref="PropertyId"/> order, with disagreement tracked per axis.
    /// </summary>
    public static void Describe(
        IReadOnlyList<SceneNode> nodes, List<PropertyRow> into, EntitySchemaCatalog? schemas = null,
        int pickedPlane = -1)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();
        if (nodes.Count == 0)
            return;

        if (nodes.Count == 1)
        {
            Describe(nodes[0], into, schemas, pickedPlane);
            return;
        }

        // The picked face is dropped: a plane index means nothing across brushes.

        var merged = new SortedDictionary<RowSlot, PropertyRow>();
        var slots = new Dictionary<(PropertyId Id, string Key), RowSlot>();
        var scratch = new List<PropertyRow>();
        int keyedSeen = 0;

        foreach (SceneNode node in nodes)
        {
            ArgumentNullException.ThrowIfNull(node);
            Describe(node, scratch, schemas);

            foreach (PropertyRow row in scratch)
            {
                // Id alone would fold every entity keyvalue into one row.
                (PropertyId Id, string Key) identity = (row.Id, row.Key ?? "");

                if (!slots.TryGetValue(identity, out RowSlot slot))
                {
                    // Keys under one id keep first-seen order, which for nodes
                    // of one class is the schema's declaration order.
                    slot = new RowSlot(row.Id, identity.Key.Length == 0 ? 0 : ++keyedSeen);
                    slots.Add(identity, slot);
                    merged.Add(slot, row with { PresentCount = 1, SelectionCount = nodes.Count });
                    continue;
                }

                PropertyRow existing = merged[slot];
                merged[slot] = existing with
                {
                    PresentCount = existing.PresentCount + 1,
                    MixedAxes = existing.MixedAxes | Disagreement(existing, row),
                };
            }
        }

        foreach (PropertyRow row in merged.Values)
            into.Add(row);
    }

    // Sort position of a merged row: property first, then first appearance of its key.
    private readonly record struct RowSlot(PropertyId Id, int Order) : IComparable<RowSlot>
    {
        public int CompareTo(RowSlot other)
        {
            int byProperty = ((int)Id).CompareTo((int)other.Id);
            return byProperty != 0 ? byProperty : Order.CompareTo(other.Order);
        }
    }

    // Exact comparison, no tolerance: values reported equal get written over
    // each other by the next bulk edit.
    private static PropertyAxes Disagreement(in PropertyRow a, in PropertyRow b) => a.Kind switch
    {
        PropertyKind.Vector3 or PropertyKind.Color =>
            (a.Vector.X == b.Vector.X ? PropertyAxes.None : PropertyAxes.X)
            | (a.Vector.Y == b.Vector.Y ? PropertyAxes.None : PropertyAxes.Y)
            | (a.Vector.Z == b.Vector.Z ? PropertyAxes.None : PropertyAxes.Z),

        PropertyKind.Number => a.Number == b.Number ? PropertyAxes.None : PropertyAxes.All,
        PropertyKind.Boolean => a.Flag == b.Flag ? PropertyAxes.None : PropertyAxes.All,

        _ => string.Equals(a.Text, b.Text, StringComparison.Ordinal)
            ? PropertyAxes.None
            : PropertyAxes.All,
    };

    private static void DescribeBrush(SceneNode node, Brush brush, List<PropertyRow> into)
    {
        into.Add(PropertyRow.OfChoice(
            BrushGroup, "Kind", PropertyId.BrushKind,
            node.BrushKind == BrushKind.Part ? "Part" : "World",
            BrushKindChoices, BrushKindLabels, BrushKindHelp));

        into.Add(PropertyRow.OfChoice(
            BrushGroup, "Operation", PropertyId.BrushOperation,
            brush.Operation == BrushOperation.Subtractive ? "Subtractive" : "Additive",
            BrushOperationChoices, BrushOperationLabels, BrushOperationHelp));

        // Bounds size, the same measurement the resize gizmo works in.
        Aabb bounds = brush.LocalBounds;
        into.Add(PropertyRow.OfVector(BrushGroup, "Size", PropertyId.BrushSize, bounds.Max - bounds.Min, "su"));
    }

    /// <summary>
    /// Whether the node shows the Collides, Seen by queries and Touch events
    /// rows: it has a brush or a mesh for them to apply to.
    /// </summary>
    public static bool HasFlagRows(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Brush is not null || node.MeshRenderer is not null;
    }

    /// <summary>
    /// Whether the node shows the Drawn row: it draws through its own mesh or
    /// additive part brush.
    /// </summary>
    // Not a world brush: the static world draws that whatever the node says.
    public static bool HasDrawnRow(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.MeshRenderer is not null ||
               (node.BrushKind == BrushKind.Part &&
                node.Brush is { Operation: BrushOperation.Additive });
    }

    private static void DescribeFlags(SceneNode node, List<PropertyRow> into)
    {
        into.Add(PropertyRow.OfFlag(BehaviorGroup, "Collides", PropertyId.CanCollide, node.CanCollide));
        into.Add(PropertyRow.OfFlag(BehaviorGroup, "Seen by queries", PropertyId.CanQuery, node.CanQuery));
        into.Add(PropertyRow.OfFlag(BehaviorGroup, "Touch events", PropertyId.CanTouch, node.CanTouch));

        if (HasDrawnRow(node))
            into.Add(PropertyRow.OfFlag(BehaviorGroup, "Drawn", PropertyId.IsRendered, node.IsRendered));
    }

    private static readonly string[] FaceAlignmentChoices = ["World", "Face"];

    private const string FaceAlignmentHelp =
        "World: the texture projects from the world axes, so it stays put when the brush turns. " +
        "Face: it lies in the face's own plane and turns with it.";

    // The brush's material, plus the picked face's material and texture frame.
    private static void DescribeMaterial(SceneNode node, Brush brush, int pickedPlane, List<PropertyRow> into)
    {
        IReadOnlyList<FaceSurface> faces = brush.FaceSurfaces;

        MaterialRef first = faces.Count > 0 ? faces[0].Material : MaterialRef.Default;
        int distinct = 1;
        for (int i = 1; i < faces.Count; i++)
        {
            if (!faces[i].Material.Equals(first)) distinct++;
        }

        bool agree = distinct == 1;
        string path = agree ? PathOf(first) : string.Empty;
        string note = agree
            ? MissingNote(node, first)
            : $"mixed ({CountDistinct(faces)} materials)";

        into.Add(PropertyRow.OfAsset(
            MaterialGroup, "Material", PropertyId.BrushMaterial, path, AssetKind.Material, note));

        if (pickedPlane < 0 || pickedPlane >= faces.Count) return;

        FaceSurface face = faces[pickedPlane];
        Vector3 normal = FaceAxes.WorldNormal(brush.LocalPlanes[pickedPlane], node.WorldMatrix);
        FaceSurface world = face.Transformed(node.WorldMatrix);
        string key = pickedPlane.ToString(CultureInfo.InvariantCulture);

        into.Add(PropertyRow.ReadOnly(
            FaceGroup, "Face", PropertyId.FaceIndex, FaceLabel(brush, pickedPlane), key));

        into.Add(PropertyRow.OfAsset(
            FaceGroup, "Material", PropertyId.FaceMaterial, PathOf(face.Material),
            AssetKind.Material, MissingNote(node, face.Material), key));

        into.Add(PropertyRow.OfChoice(
            FaceGroup, "Alignment", PropertyId.FaceAlignment,
            FaceAxes.AlignmentLabel(in world),
            FaceAlignmentChoices, help: FaceAlignmentHelp, key: key));

        into.Add(PropertyRow.OfNumber(FaceGroup, "U scale", PropertyId.FaceUScale, face.UScale, "su/rep", key));
        into.Add(PropertyRow.OfNumber(FaceGroup, "V scale", PropertyId.FaceVScale, face.VScale, "su/rep", key));
        into.Add(PropertyRow.OfNumber(FaceGroup, "U offset", PropertyId.FaceUOffset, face.UOffset, "rep", key));
        into.Add(PropertyRow.OfNumber(FaceGroup, "V offset", PropertyId.FaceVOffset, face.VOffset, "rep", key));
        into.Add(PropertyRow.OfNumber(
            FaceGroup, "Rotation", PropertyId.FaceRotation,
            FaceAxes.RotationDegrees(in world, normal), "deg", key));
    }

    private static int CountDistinct(IReadOnlyList<FaceSurface> faces)
    {
        int count = 0;
        for (int i = 0; i < faces.Count; i++)
        {
            bool seen = false;
            for (int j = 0; j < i; j++)
            {
                if (faces[j].Material.Equals(faces[i].Material)) { seen = true; break; }
            }

            if (!seen) count++;
        }

        return count;
    }

    private static string PathOf(MaterialRef material) =>
        MaterialRegistry.TryGetPath(material, out string path) ? path : string.Empty;

    // Asks the asset manager's cache, not the disk: this runs per publish.
    private static string MissingNote(SceneNode node, MaterialRef material)
    {
        if (material.IsDefault) return string.Empty;
        if (node.Owner?.Assets is not { } assets) return string.Empty;

        return assets.IsMaterialMissing(PathOf(material)) ? "missing" : string.Empty;
    }

    private static string FaceLabel(Brush brush, int planeIndex)
    {
        Vector3 n = brush.LocalPlanes[planeIndex].Normal;

        if (n.X > 0.999f) return "+X";
        if (n.X < -0.999f) return "-X";
        if (n.Y > 0.999f) return "+Y";
        if (n.Y < -0.999f) return "-Y";
        if (n.Z > 0.999f) return "+Z";
        if (n.Z < -0.999f) return "-Z";

        return "plane " + planeIndex.ToString(CultureInfo.InvariantCulture);
    }

    private static void DescribeLight(Light light, List<PropertyRow> into)
    {
        into.Add(PropertyRow.OfChoice(
            LightGroup, "Kind", PropertyId.LightKind, KindLabel(light.Kind), LightKindChoices));

        // Linear RGB.
        into.Add(PropertyRow.OfColor(LightGroup, "Color", PropertyId.LightColor, light.Color));
        into.Add(PropertyRow.OfNumber(LightGroup, "Intensity", PropertyId.LightIntensity, light.Intensity));

        // Shown for directional lights too: Range is still validated on set.
        into.Add(PropertyRow.OfNumber(LightGroup, "Range", PropertyId.LightRange, light.Range, "su"));
        into.Add(PropertyRow.OfFlag(LightGroup, "Enabled", PropertyId.LightEnabled, light.Enabled));

        // Shape rows only for the kind that reads them.
        switch (light.Kind)
        {
            case LightKind.Spot:
                into.Add(PropertyRow.OfNumber(
                    LightGroup, "Inner angle", PropertyId.LightInnerAngle, light.InnerAngle, "deg"));
                into.Add(PropertyRow.OfNumber(
                    LightGroup, "Outer angle", PropertyId.LightOuterAngle, light.OuterAngle, "deg"));
                break;

            case LightKind.Rect:
                into.Add(PropertyRow.OfNumber(
                    LightGroup, "Width", PropertyId.LightWidth, light.Width, "su"));
                into.Add(PropertyRow.OfNumber(
                    LightGroup, "Height", PropertyId.LightHeight, light.Height, "su"));
                break;

            case LightKind.Disc:
                into.Add(PropertyRow.OfNumber(
                    LightGroup, "Radius", PropertyId.LightRadius, light.Radius, "su"));
                break;
        }
    }

    // Schema-declared keys show their stored value or the declared default.
    // Stored keys the schema does not name are shown as text, so an unknown
    // class is still editable.
    private static void DescribeEntity(
        EntityData entity, EntitySchemaCatalog? schemas, List<PropertyRow> into)
    {
        // Read-only: retyping a class would orphan every keyvalue it named.
        into.Add(PropertyRow.ReadOnly(
            EntityGroup, "Class", PropertyId.EntityClassname, entity.ClassName));

        int entityStart = into.Count;

        EntitySchema? schema = null;
        schemas?.TryGetSchema(entity.ClassName, out schema);

        if (schema is not null)
        {
            IReadOnlyList<KeyvalueDescriptor> declared = schema.Keyvalues;
            for (int i = 0; i < declared.Count; i++)
            {
                KeyvalueDescriptor descriptor = declared[i];

                // No row, but IsDeclared below still has to see it or the key
                // comes back as an unknown one.
                if (descriptor.IsHiddenInEditor)
                    continue;

                string value = entity.TryGetValue(descriptor.Name, out string stored)
                    ? stored
                    : descriptor.Default;

                into.Add(RowFor(descriptor, value));
            }
        }

        foreach (KeyValuePair<string, string> keyvalue in entity.Keyvalues)
        {
            if (schema is not null && IsDeclared(schema, keyvalue.Key))
                continue;

            // A hand-written file can carry a key twice. First wins, matching
            // EntityData.TryGetValue.
            if (AlreadyListed(into, entityStart, keyvalue.Key))
                continue;

            into.Add(PropertyRow.OfText(
                EntityGroup, keyvalue.Key, PropertyId.EntityKeyvalue, keyvalue.Value, keyvalue.Key));
        }
    }

    private static bool IsDeclared(EntitySchema schema, string key)
    {
        IReadOnlyList<KeyvalueDescriptor> declared = schema.Keyvalues;
        for (int i = 0; i < declared.Count; i++)
        {
            if (string.Equals(declared[i].Name, key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool AlreadyListed(List<PropertyRow> rows, int from, string key)
    {
        for (int i = from; i < rows.Count; i++)
        {
            if (string.Equals(rows[i].Key, key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // A value the declared type cannot parse becomes a text row. A typed row
    // would show zero and write it back on the next commit.
    private static PropertyRow RowFor(in KeyvalueDescriptor descriptor, string value)
    {
        string label = descriptor.Display.Length > 0 ? descriptor.Display : descriptor.Name;
        string key = descriptor.Name;

        if (descriptor.IsReadOnly)
            return PropertyRow.ReadOnly(EntityGroup, label, PropertyId.EntityKeyvalue, value, key);

        if (!KeyvalueWire.IsWellFormed(descriptor.Type, value))
            return PropertyRow.OfText(EntityGroup, label, PropertyId.EntityKeyvalue, value, key);

        switch (descriptor.Type)
        {
            case KeyvalueType.Bool:
                KeyvalueWire.TryParseBool(value, out bool flag);
                return PropertyRow.OfFlag(EntityGroup, label, PropertyId.EntityKeyvalue, flag, key);

            case KeyvalueType.Int:
                KeyvalueWire.TryParseInt(value, out int whole);
                return PropertyRow.OfNumber(
                    EntityGroup, label, PropertyId.EntityKeyvalue, whole, "", key);

            case KeyvalueType.Float:
            case KeyvalueType.Distance:
                KeyvalueWire.TryParseFloat(value, out float number);
                return PropertyRow.OfNumber(
                    EntityGroup, label, PropertyId.EntityKeyvalue, number,
                    descriptor.Type == KeyvalueType.Distance ? "su" : "", key);

            case KeyvalueType.Vec3:
                KeyvalueWire.TryParseVec3(value, out Vector3 vector);
                return PropertyRow.OfVector(
                    EntityGroup, label, PropertyId.EntityKeyvalue, vector, "", key);

            case KeyvalueType.Angles:
                KeyvalueWire.TryParseAngles(value, out Vector3 degrees);
                return PropertyRow.OfVector(
                    EntityGroup, label, PropertyId.EntityKeyvalue, degrees, "deg", key);

            case KeyvalueType.Color:
                KeyvalueWire.TryParseColor(value, out Vector3 linear);
                return PropertyRow.OfColor(
                    EntityGroup, label, PropertyId.EntityKeyvalue, linear, key);

            case KeyvalueType.Choices:
                // Tokens, not display names: the row's value is the wire string
                // and the dropdown matches by text.
                return PropertyRow.OfChoice(
                    EntityGroup, label, PropertyId.EntityKeyvalue, value,
                    ChoiceTokensOf(descriptor.Choices), key: key);

            // TargetName only. A NodeRef's wire form is a GUID, so the name
            // picker would write a value the reader refuses.
            case KeyvalueType.TargetName:
                return PropertyRow.OfTarget(EntityGroup, label, PropertyId.EntityKeyvalue, value, key);

            case KeyvalueType.AssetMaterial:
                return PropertyRow.OfAsset(
                    EntityGroup, label, PropertyId.EntityKeyvalue, value, AssetKind.Material, key: key);

            case KeyvalueType.AssetTexture:
                return PropertyRow.OfAsset(
                    EntityGroup, label, PropertyId.EntityKeyvalue, value, AssetKind.Texture, key: key);

            case KeyvalueType.AssetModel:
                return PropertyRow.OfAsset(
                    EntityGroup, label, PropertyId.EntityKeyvalue, value, AssetKind.Model, key: key);

            // AssetSound stays text: the content browser has no sound kind.
            default:
                return PropertyRow.OfText(EntityGroup, label, PropertyId.EntityKeyvalue, value, key);
        }
    }

    private static string[] ChoiceTokensOf(IReadOnlyList<(string Value, string Display)> choices)
    {
        if (choices is null || choices.Count == 0)
            return [];

        return ChoiceTokenCache.GetValue(choices, static list =>
        {
            var declared = (IReadOnlyList<(string Value, string Display)>)list;
            var tokens = new string[declared.Count];
            for (int i = 0; i < tokens.Length; i++)
                tokens[i] = declared[i].Value;

            return tokens;
        });
    }

    // Every kind needs its own case: the dropdown matches by text, so a kind
    // falling to the default would be rewritten to Directional on the next edit.
    private static string KindLabel(LightKind kind) => kind switch
    {
        LightKind.Point => "Point",
        LightKind.Spot => "Spot",
        LightKind.Rect => "Rect",
        LightKind.Disc => "Disc",
        _ => "Directional",
    };
}
