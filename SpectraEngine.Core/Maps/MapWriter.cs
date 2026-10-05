using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Serialization;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SpectraEngine.Core.Maps;

/// <summary>
/// Writes a <see cref="MapDocument"/> as canonical UTF-8 JSON: the same bytes
/// for the same document on every platform.
/// </summary>
// Records that are only numbers (a plane, a face, a transform) go on one line
// so a changed plane is a one-line diff. They are rendered by a second,
// un-indented Utf8JsonWriter and emitted with WriteRawValue, so number
// formatting and escaping match the indented path.
public static class MapWriter
{
    /// <summary>Renders <paramref name="document"/> to canonical UTF-8 bytes, with no BOM.</summary>
    public static byte[] Write(MapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return CanonicalJson.Write(writer => WriteDocument(writer, document));
    }

    private static void WriteDocument(Utf8JsonWriter writer, MapDocument document)
    {
        writer.WriteStartObject();

        CanonicalJson.Flush(writer, document.Unknown, -1);

        writer.WriteNumber(MapFormat.FormatVersionMember, document.FormatVersion);
        CanonicalJson.Flush(writer, document.Unknown, 0);

        writer.WriteNumber(MapFormat.MinimumReadableMember, document.MinimumReadableVersion);
        CanonicalJson.Flush(writer, document.Unknown, 1);

        writer.WriteString(MapFormat.EngineMember, document.Engine);
        CanonicalJson.Flush(writer, document.Unknown, 2);

        writer.WritePropertyName(MapFormat.SceneMember);
        WriteSceneInfo(writer, document.Scene);
        CanonicalJson.Flush(writer, document.Unknown, 3);

        if (document.Editor is { } editor)
        {
            writer.WritePropertyName(MapFormat.EditorMember);
            writer.WriteRawValue(editor.Raw);
        }
        CanonicalJson.Flush(writer, document.Unknown, 4);

        writer.WritePropertyName(MapFormat.NodesMember);
        writer.WriteStartArray();
        foreach (MapNode node in document.Nodes)
            WriteNode(writer, node);
        writer.WriteEndArray();
        CanonicalJson.Flush(writer, document.Unknown, 5);

        writer.WriteEndObject();
    }

    private static void WriteSceneInfo(Utf8JsonWriter writer, MapSceneInfo scene)
    {
        writer.WriteStartObject();
        CanonicalJson.Flush(writer, scene.Unknown, -1);
        writer.WriteString(MapFormat.NameMember, scene.Name);
        CanonicalJson.Flush(writer, scene.Unknown, 0);
        writer.WriteEndObject();
    }

    private static void WriteNode(Utf8JsonWriter writer, MapNode node)
    {
        writer.WriteStartObject();

        CanonicalJson.Flush(writer, node.Unknown, -1);

        writer.WriteString(MapFormat.IdMember, node.Id.ToString("D"));
        CanonicalJson.Flush(writer, node.Unknown, 0);

        writer.WriteString(MapFormat.NameMember, node.Name);
        CanonicalJson.Flush(writer, node.Unknown, 1);

        // Omitted means inherit.
        if (node.Realm is { } realm)
            writer.WriteString(MapFormat.RealmMember, realm);
        CanonicalJson.Flush(writer, node.Unknown, 2);

        if (node.State is { } state)
            writer.WriteString(MapFormat.StateMember, state);
        CanonicalJson.Flush(writer, node.Unknown, 3);

        // Omitted when World. A name, not a number, so renumbering the enum
        // cannot change a file's meaning.
        if (node.Kind is { } kind && kind != BrushKind.World)
            writer.WriteString(MapFormat.KindMember, MapFormat.ToWire(kind));
        CanonicalJson.Flush(writer, node.Unknown, 4);

        // Each written only when off, so a node with default flags writes
        // nothing here.
        if (!node.Collide) writer.WriteBoolean(MapFormat.CollideMember, false);
        CanonicalJson.Flush(writer, node.Unknown, 5);

        if (!node.Query) writer.WriteBoolean(MapFormat.QueryMember, false);
        CanonicalJson.Flush(writer, node.Unknown, 6);

        if (!node.Touch) writer.WriteBoolean(MapFormat.TouchMember, false);
        CanonicalJson.Flush(writer, node.Unknown, 7);

        if (!node.Render) writer.WriteBoolean(MapFormat.RenderMember, false);
        CanonicalJson.Flush(writer, node.Unknown, 8);

        writer.WritePropertyName(MapFormat.TransformMember);
        writer.WriteRawValue(CompactTransform(node.Transform));
        CanonicalJson.Flush(writer, node.Unknown, 9);

        if (node.Brush is { } brush)
        {
            writer.WritePropertyName(MapFormat.BrushMember);
            WriteBrush(writer, brush);
        }
        CanonicalJson.Flush(writer, node.Unknown, 10);

        if (node.Mesh is { } mesh)
        {
            writer.WritePropertyName(MapFormat.MeshMember);
            writer.WriteRawValue(CompactMesh(mesh));
        }
        CanonicalJson.Flush(writer, node.Unknown, 11);

        if (node.Light is { } light)
        {
            writer.WritePropertyName(MapFormat.LightMember);
            writer.WriteRawValue(CompactLight(light));
        }
        CanonicalJson.Flush(writer, node.Unknown, 12);

        if (node.Entity is { } entity)
        {
            writer.WritePropertyName(MapFormat.EntityMember);
            WriteEntity(writer, entity);
        }
        CanonicalJson.Flush(writer, node.Unknown, 13);

        if (node.Editor is { } editor)
        {
            writer.WritePropertyName(MapFormat.EditorMember);
            writer.WriteRawValue(editor.Raw);
        }
        CanonicalJson.Flush(writer, node.Unknown, 14);

        writer.WritePropertyName(MapFormat.ChildrenMember);
        writer.WriteStartArray();
        foreach (MapNode child in node.Children)
            WriteNode(writer, child);
        writer.WriteEndArray();
        CanonicalJson.Flush(writer, node.Unknown, 15);

        writer.WriteEndObject();
    }

    private static void WriteBrush(Utf8JsonWriter writer, MapBrush brush)
    {
        writer.WriteStartObject();

        // First in the record so it is easy to spot in a diff. Omitted when Additive.
        if (brush.Operation != BrushOperation.Additive)
            writer.WriteString(MapFormat.OperationMember, MapFormat.ToWire(brush.Operation));

        if (brush.Transform != Matrix4x4.Identity)
        {
            writer.WritePropertyName(MapFormat.BrushTransformMember);
            writer.WriteRawValue(CompactMatrix(brush.Transform));
        }

        var planes = new List<byte[]>(brush.Planes.Count);
        foreach (Vector4 plane in brush.Planes)
            planes.Add(CompactNumbers(plane.X, plane.Y, plane.Z, plane.W));
        CanonicalJson.WriteRecordArray(writer, MapFormat.PlanesMember, planes);

        var faces = new List<byte[]>(brush.Faces.Count);
        foreach (MapFace face in brush.Faces)
            faces.Add(CompactFace(face));
        CanonicalJson.WriteRecordArray(writer, MapFormat.FacesMember, faces);

        if (brush.KeepSource)
            writer.WriteBoolean(MapFormat.KeepSourceMember, true);

        writer.WriteEndObject();
    }

    // Indented like a brush, not compact like a light: the class, the keyvalue
    // set and each wire are edited on their own, so each gets its own line.
    private static void WriteEntity(Utf8JsonWriter writer, MapEntity entity)
    {
        writer.WriteStartObject();
        CanonicalJson.Flush(writer, entity.Unknown, -1);

        // Always written, even when empty: the reader refuses a record without it.
        writer.WriteString(MapFormat.ClassMember, entity.Class);
        CanonicalJson.Flush(writer, entity.Unknown, 0);

        if (entity.Keys.Count > 0)
        {
            writer.WritePropertyName(MapFormat.KeysMember);
            writer.WriteRawValue(CompactKeys(entity.Keys));
        }
        CanonicalJson.Flush(writer, entity.Unknown, 1);

        if (entity.Outputs.Count > 0)
        {
            var outputs = new List<byte[]>(entity.Outputs.Count);
            foreach (MapConnection connection in entity.Outputs)
                outputs.Add(CompactConnection(connection));
            CanonicalJson.WriteRecordArray(writer, MapFormat.OutputsMember, outputs);
        }
        CanonicalJson.Flush(writer, entity.Unknown, 2);

        writer.WriteEndObject();
    }

    private const int IndentSize = 2;

    private static byte[] CompactTransform(MapTransform transform) => CanonicalJson.Compact(w =>
    {
        w.WriteStartObject();

        // Always written, even at the origin.
        w.WritePropertyName(MapFormat.PositionMember);
        WriteNumbers(w, transform.Position.X, transform.Position.Y, transform.Position.Z);

        if (transform.Rotation != Quaternion.Identity)
        {
            w.WritePropertyName(MapFormat.RotationMember);
            WriteNumbers(w, transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W);
        }

        if (transform.Scale != Vector3.One)
        {
            w.WritePropertyName(MapFormat.ScaleMember);
            WriteNumbers(w, transform.Scale.X, transform.Scale.Y, transform.Scale.Z);
        }

        w.WriteEndObject();
    });

    private static byte[] CompactFace(MapFace face) => CanonicalJson.Compact(w =>
    {
        w.WriteStartObject();
        CanonicalJson.Flush(w, face.Unknown, -1);

        // Absent means the default material, which has no path.
        if (!string.IsNullOrEmpty(face.Material))
            w.WriteString(MapFormat.MaterialMember, face.Material);
        CanonicalJson.Flush(w, face.Unknown, 0);

        // A world-aligned face omits both axes.
        if (face.UAxis is { } u)
        {
            w.WritePropertyName(MapFormat.UAxisMember);
            WriteNumbers(w, u.X, u.Y, u.Z);
        }
        CanonicalJson.Flush(w, face.Unknown, 1);

        if (face.VAxis is { } v)
        {
            w.WritePropertyName(MapFormat.VAxisMember);
            WriteNumbers(w, v.X, v.Y, v.Z);
        }
        CanonicalJson.Flush(w, face.Unknown, 2);

        if (face.UOffset != 0f) WriteFinite(w, MapFormat.UOffsetMember, face.UOffset);
        CanonicalJson.Flush(w, face.Unknown, 3);

        if (face.VOffset != 0f) WriteFinite(w, MapFormat.VOffsetMember, face.VOffset);
        CanonicalJson.Flush(w, face.Unknown, 4);

        if (face.UScale != 1f) WriteFinite(w, MapFormat.UScaleMember, face.UScale);
        CanonicalJson.Flush(w, face.Unknown, 5);

        if (face.VScale != 1f) WriteFinite(w, MapFormat.VScaleMember, face.VScale);
        CanonicalJson.Flush(w, face.Unknown, 6);

        w.WriteEndObject();
    });

    private static byte[] CompactMesh(MapMeshSource mesh) => CanonicalJson.Compact(w =>
    {
        w.WriteStartObject();
        CanonicalJson.Flush(w, mesh.Unknown, -1);

        w.WriteString(MapFormat.ModelMember, mesh.Model);
        CanonicalJson.Flush(w, mesh.Unknown, 0);

        if (mesh.Submesh != 0)
            w.WriteNumber(MapFormat.SubmeshMember, mesh.Submesh);
        CanonicalJson.Flush(w, mesh.Unknown, 1);

        w.WriteEndObject();
    });

    private static byte[] CompactLight(MapLight light) => CanonicalJson.Compact(w =>
    {
        w.WriteStartObject();
        CanonicalJson.Flush(w, light.Unknown, -1);

        if (light.Kind != LightKind.Directional)
            w.WriteString(MapFormat.KindMember, MapFormat.ToWire(light.Kind));
        CanonicalJson.Flush(w, light.Unknown, 0);

        if (light.Color != Vector3.One)
        {
            w.WritePropertyName(MapFormat.ColorMember);
            WriteNumbers(w, light.Color.X, light.Color.Y, light.Color.Z);
        }
        CanonicalJson.Flush(w, light.Unknown, 1);

        if (light.Intensity != 1f) WriteFinite(w, MapFormat.IntensityMember, light.Intensity);
        CanonicalJson.Flush(w, light.Unknown, 2);

        // Never zero on disk: Light.Range throws on anything not positive.
        if (light.Range != 10f) WriteFinite(w, MapFormat.RangeMember, light.Range);
        CanonicalJson.Flush(w, light.Unknown, 3);

        if (!light.Enabled) w.WriteBoolean(MapFormat.EnabledMember, false);
        CanonicalJson.Flush(w, light.Unknown, 4);

        // Written only when not the default, so a file without these members
        // still round-trips byte for byte.
        if (light.InnerAngle != 25f) WriteFinite(w, MapFormat.InnerAngleMember, light.InnerAngle);
        CanonicalJson.Flush(w, light.Unknown, 5);

        if (light.OuterAngle != 35f) WriteFinite(w, MapFormat.OuterAngleMember, light.OuterAngle);
        CanonicalJson.Flush(w, light.Unknown, 6);

        if (light.Width != 1f) WriteFinite(w, MapFormat.WidthMember, light.Width);
        CanonicalJson.Flush(w, light.Unknown, 7);

        if (light.Height != 1f) WriteFinite(w, MapFormat.HeightMember, light.Height);
        CanonicalJson.Flush(w, light.Unknown, 8);

        if (light.Radius != 0.5f) WriteFinite(w, MapFormat.RadiusMember, light.Radius);
        CanonicalJson.Flush(w, light.Unknown, 9);

        w.WriteEndObject();
    });

    // One compact object in authored order. Don't sort it and don't drop
    // duplicates: both would rewrite a hand-edited file.
    private static byte[] CompactKeys(List<KeyValuePair<string, string>> keys) =>
        CanonicalJson.Compact(w =>
        {
            w.WriteStartObject();
            foreach (KeyValuePair<string, string> pair in keys)
                w.WriteString(pair.Key, pair.Value);
            w.WriteEndObject();
        });

    private static byte[] CompactConnection(MapConnection connection) => CanonicalJson.Compact(w =>
    {
        w.WriteStartObject();
        CanonicalJson.Flush(w, connection.Unknown, -1);

        // Output, target and input are always written, even when empty.
        w.WriteString(MapFormat.OutputMember, connection.Output);
        CanonicalJson.Flush(w, connection.Unknown, 0);

        w.WriteString(MapFormat.TargetMember, connection.Target);
        CanonicalJson.Flush(w, connection.Unknown, 1);

        w.WriteString(MapFormat.InputMember, connection.Input);
        CanonicalJson.Flush(w, connection.Unknown, 2);

        if (connection.Param.Length > 0)
            w.WriteString(MapFormat.ParamMember, connection.Param);
        CanonicalJson.Flush(w, connection.Unknown, 3);

        if (connection.Delay != 0f)
            WriteFinite(w, MapFormat.DelayMember, connection.Delay);
        CanonicalJson.Flush(w, connection.Unknown, 4);

        if (connection.Times != Entities.EntityConnection.Infinite)
            w.WriteNumber(MapFormat.TimesMember, connection.Times);
        CanonicalJson.Flush(w, connection.Unknown, 5);

        w.WriteEndObject();
    });

    private static byte[] CompactMatrix(Matrix4x4 m) => CompactNumbers(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);

    private static byte[] CompactNumbers(params float[] values) =>
        CanonicalJson.Compact(w => WriteNumbers(w, values));

    private static void WriteNumbers(Utf8JsonWriter writer, params float[] values)
    {
        writer.WriteStartArray();
        foreach (float value in values)
            writer.WriteNumberValue(Finite(value, "array element"));
        writer.WriteEndArray();
    }

    private static void WriteFinite(Utf8JsonWriter writer, string member, float value) =>
        writer.WriteNumber(member, Finite(value, member));

    // JSON has no NaN or infinity. Refusing here names the member, which
    // Utf8JsonWriter's own exception does not.
    private static float Finite(float value, string member) =>
        float.IsFinite(value)
            ? value
            : throw new MapFormatException(
                $"Cannot write '{member}': {value} has no JSON representation.", null, 0);
}
