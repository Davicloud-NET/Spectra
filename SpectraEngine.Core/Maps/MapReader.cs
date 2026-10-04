using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace SpectraEngine.Core.Maps;

/// <summary>
/// Reads a canonical map document into a <see cref="MapDocument"/>, carrying
/// through every member it does not recognise.
/// </summary>
// The document must be one contiguous span. Preservation slices the source
// between TokenStartIndex and BytesConsumed, which are relative to the reader's
// own input; chunked input would slice the wrong bytes without failing.
public static class MapReader
{
    /// <summary>Parses a canonical map document.</summary>
    /// <exception cref="MapFormatException">The document is malformed, or names a value outside a closed vocabulary.</exception>
    public static MapDocument Read(ReadOnlySpan<byte> utf8)
    {
        utf8 = CanonicalJson.StripBom(utf8);

        var reader = new Utf8JsonReader(utf8, MapFormat.ReaderOptions);
        var state = new ReadState();

        try
        {
            var document = new MapDocument();
            ReadDocument(ref reader, utf8, document, state);
            return document;
        }
        catch (MapFormatException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new MapFormatException(
                $"The map document is not valid JSON: {ex.Message}",
                state.NodeName, ex.BytePositionInLine ?? reader.TokenStartIndex, ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new MapFormatException(ex.Message, state.NodeName, reader.TokenStartIndex, ex);
        }
    }

    private sealed class ReadState
    {
        public string? NodeName;
    }

    private static void ReadDocument(
        ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, MapDocument document, ReadState state)
    {
        Expect(ref reader, JsonTokenType.StartObject, "the document root must be an object", state);

        bool sawMinimumReadable = false;
        int anchor = -1;

        while (NextMember(ref reader, out string member))
        {
            switch (member)
            {
                case MapFormat.FormatVersionMember:
                    document.FormatVersion = ReadInt(ref reader, member, state);
                    anchor = 0;
                    break;

                case MapFormat.MinimumReadableMember:
                    document.MinimumReadableVersion = ReadInt(ref reader, member, state);
                    RefuseUnreadable(document.MinimumReadableVersion, ref reader, state);
                    sawMinimumReadable = true;
                    anchor = 1;
                    break;

                case MapFormat.EngineMember:
                    document.Engine = ReadString(ref reader, member, state);
                    anchor = 2;
                    break;

                case MapFormat.SceneMember:
                    ReadSceneInfo(ref reader, utf8, document.Scene, state);
                    anchor = 3;
                    break;

                case MapFormat.EditorMember:
                    document.Editor = new PreservedValue(CanonicalJson.CaptureValue(ref reader, utf8));
                    anchor = 4;
                    break;

                case MapFormat.NodesMember:
                    ReadNodeArray(ref reader, utf8, document.Nodes, state);
                    anchor = 5;
                    break;

                default:
                    document.Unknown.Add(Preserve(ref reader, utf8, member, anchor));
                    break;
            }
        }

        if (!sawMinimumReadable)
            RefuseUnreadable(document.MinimumReadableVersion, ref reader, state);
    }

    // A newer document is fine, since unknown members are carried. One whose
    // minimumReadableVersion is above this engine's is refused.
    private static void RefuseUnreadable(int minimumReadable, ref Utf8JsonReader reader, ReadState state)
    {
        if (minimumReadable > EngineInfo.MapFormatVersion)
        {
            throw new MapFormatException(
                $"This map needs a reader for map format {minimumReadable}; this engine implements "
                + $"{EngineInfo.MapFormatVersion}.", state.NodeName, reader.TokenStartIndex);
        }
    }

    private static void ReadSceneInfo(
        ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, MapSceneInfo scene, ReadState state)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'scene' must be an object", state);

        int anchor = -1;
        while (NextMember(ref reader, out string member))
        {
            if (member == MapFormat.NameMember)
            {
                scene.Name = ReadString(ref reader, member, state);
                anchor = 0;
            }
            else
            {
                // Includes 'spawn': Scene has nothing to bind it to yet.
                scene.Unknown.Add(Preserve(ref reader, utf8, member, anchor));
            }
        }
    }

    private static void ReadNodeArray(
        ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, List<MapNode> into, ReadState state)
    {
        Expect(ref reader, JsonTokenType.StartArray, "a node list must be an array", state);

        while (Read(ref reader, state) && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw Fail(ref reader, "a node must be an object", state);

            into.Add(ReadNode(ref reader, utf8, state));
        }
    }

    private static MapNode ReadNode(ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, ReadState state)
    {
        var node = new MapNode { SourceOffset = reader.TokenStartIndex };
        string? outerName = state.NodeName;
        int anchor = -1;

        while (NextMember(ref reader, out string member))
        {
            switch (member)
            {
                case MapFormat.IdMember:
                    node.Id = ReadGuid(ref reader, state);
                    anchor = 0;
                    break;

                case MapFormat.NameMember:
                    node.Name = ReadString(ref reader, member, state);
                    // Later errors inside this node name it.
                    state.NodeName = node.Name;
                    anchor = 1;
                    break;

                case MapFormat.RealmMember:
                    node.Realm = ReadVocabulary(ref reader, MapFormat.Realms, member, state);
                    anchor = 2;
                    break;

                case MapFormat.StateMember:
                    node.State = ReadVocabulary(ref reader, MapFormat.States, member, state);
                    anchor = 3;
                    break;

                case MapFormat.KindMember:
                    node.Kind = ReadBrushKind(ref reader, state);
                    anchor = 4;
                    break;

                case MapFormat.TransformMember:
                    node.Transform = ReadTransform(ref reader, state);
                    anchor = 5;
                    break;

                case MapFormat.BrushMember:
                    node.Brush = ReadBrush(ref reader, utf8, state);
                    anchor = 6;
                    break;

                case MapFormat.MeshMember:
                    node.Mesh = ReadMeshSource(ref reader, utf8, state);
                    anchor = 7;
                    break;

                case MapFormat.LightMember:
                    node.Light = ReadLight(ref reader, utf8, state);
                    anchor = 8;
                    break;

                case MapFormat.EntityMember:
                    node.Entity = ReadEntity(ref reader, utf8, state);
                    anchor = 9;
                    break;

                case MapFormat.EditorMember:
                    node.Editor = new PreservedValue(CanonicalJson.CaptureValue(ref reader, utf8));
                    anchor = 10;
                    break;

                case MapFormat.ChildrenMember:
                    ReadNodeArray(ref reader, utf8, node.Children, state);
                    anchor = 11;
                    break;

                default:
                    // Includes 'script', which nothing in Core runs yet.
                    node.Unknown.Add(Preserve(ref reader, utf8, member, anchor));
                    break;
            }
        }

        state.NodeName = outerName;
        return node;
    }

    private static BrushKind ReadBrushKind(ref Utf8JsonReader reader, ReadState state)
    {
        string value = ReadString(ref reader, MapFormat.KindMember, state);
        return value switch
        {
            MapFormat.WorldKind => BrushKind.World,
            MapFormat.PartKind => BrushKind.Part,
            // No fall-through to World: a typo would admit a part brush to the carve.
            _ => throw Fail(ref reader,
                $"'{MapFormat.KindMember}' must be '{MapFormat.WorldKind}' or '{MapFormat.PartKind}', not '{value}'",
                state),
        };
    }

    private static string ReadVocabulary(
        ref Utf8JsonReader reader, string[] vocabulary, string member, ReadState state)
    {
        string value = ReadString(ref reader, member, state);
        if (Array.IndexOf(vocabulary, value) < 0)
        {
            throw Fail(ref reader,
                $"'{member}' must be one of {string.Join(", ", vocabulary)}; got '{value}'", state);
        }
        return value;
    }

    private static MapTransform ReadTransform(ref Utf8JsonReader reader, ReadState state)
    {
        // Identity, not default: default has zero scale and a zero quaternion.
        MapTransform transform = MapTransform.Identity;
        Expect(ref reader, JsonTokenType.StartObject, "'transform' must be an object", state);

        while (NextMember(ref reader, out string member))
        {
            switch (member)
            {
                case MapFormat.PositionMember:
                    transform.Position = ReadVector3(ref reader, member, state);
                    break;
                case MapFormat.RotationMember:
                    float[] r = ReadFloats(ref reader, 4, member, state);
                    transform.Rotation = new Quaternion(r[0], r[1], r[2], r[3]);
                    break;
                case MapFormat.ScaleMember:
                    transform.Scale = ReadVector3(ref reader, member, state);
                    break;
                default:
                    throw Fail(ref reader, $"'transform' has no member '{member}'", state);
            }
        }

        return transform;
    }

    private static MapBrush ReadBrush(ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, ReadState state)
    {
        var brush = new MapBrush();
        bool sawFaces = false;
        Expect(ref reader, JsonTokenType.StartObject, "'brush' must be an object", state);

        while (NextMember(ref reader, out string member))
        {
            switch (member)
            {
                case MapFormat.OperationMember:
                    brush.Operation = ReadOperation(ref reader, state);
                    break;

                case MapFormat.BrushTransformMember:
                    float[] m = ReadFloats(ref reader, 16, member, state);
                    brush.Transform = new Matrix4x4(
                        m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
                        m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
                    break;

                case MapFormat.PlanesMember:
                    ReadPlanes(ref reader, brush.Planes, state);
                    break;

                case MapFormat.FacesMember:
                    ReadFaces(ref reader, utf8, brush.Faces, state);
                    sawFaces = true;
                    break;

                case MapFormat.KeepSourceMember:
                    brush.KeepSource = ReadBool(ref reader, member, state);
                    break;

                default:
                    // Closed record: an ignored brush member could change the solid.
                    throw Fail(ref reader, $"'brush' has no member '{member}'", state);
            }
        }

        // Faces are indexed by plane index, so the counts must match.
        if (sawFaces && brush.Faces.Count != brush.Planes.Count)
        {
            throw Fail(ref reader,
                $"'faces' is indexed by plane, so it must have {brush.Planes.Count} entries, not {brush.Faces.Count}",
                state);
        }

        if (!sawFaces)
        {
            for (int i = 0; i < brush.Planes.Count; i++)
                brush.Faces.Add(new MapFace());
        }

        return brush;
    }

    private static BrushOperation ReadOperation(ref Utf8JsonReader reader, ReadState state)
    {
        string value = ReadString(ref reader, MapFormat.OperationMember, state);
        return value switch
        {
            MapFormat.AdditiveOperation => BrushOperation.Additive,
            MapFormat.SubtractiveOperation => BrushOperation.Subtractive,
            // No fall-through to Additive: a typo would turn a doorway into a wall.
            _ => throw Fail(ref reader,
                $"'{MapFormat.OperationMember}' must be '{MapFormat.AdditiveOperation}' or "
                + $"'{MapFormat.SubtractiveOperation}', not '{value}'", state),
        };
    }

    private static void ReadPlanes(ref Utf8JsonReader reader, List<Vector4> into, ReadState state)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'planes' must be an array", state);

        while (Read(ref reader, state) && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw Fail(ref reader, "a plane must be an array of four numbers", state);

            float[] p = ReadFloatsInArray(ref reader, 4, "a plane", state);
            into.Add(new Vector4(p[0], p[1], p[2], p[3]));
        }
    }

    private static void ReadFaces(
        ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, List<MapFace> into, ReadState state)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'faces' must be an array", state);

        while (Read(ref reader, state) && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw Fail(ref reader, "a face must be an object", state);

            into.Add(ReadFace(ref reader, utf8, state));
        }
    }

    private static MapFace ReadFace(ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, ReadState state)
    {
        var face = new MapFace();
        int anchor = -1;

        while (NextMember(ref reader, out string member))
        {
            switch (member)
            {
                case MapFormat.MaterialMember:
                    face.Material = ReadString(ref reader, member, state);
                    anchor = 0;
                    break;
                case MapFormat.UAxisMember:
                    face.UAxis = ReadVector3(ref reader, member, state);
                    anchor = 1;
                    break;
                case MapFormat.VAxisMember:
                    face.VAxis = ReadVector3(ref reader, member, state);
                    anchor = 2;
                    break;
                case MapFormat.UOffsetMember:
                    face.UOffset = ReadFloat(ref reader, member, state);
                    anchor = 3;
                    break;
                case MapFormat.VOffsetMember:
                    face.VOffset = ReadFloat(ref reader, member, state);
                    anchor = 4;
                    break;
                case MapFormat.UScaleMember:
                    face.UScale = ReadFloat(ref reader, member, state);
                    anchor = 5;
                    break;
                case MapFormat.VScaleMember:
                    face.VScale = ReadFloat(ref reader, member, state);
                    anchor = 6;
                    break;
                default:
                    // Open record, unlike 'brush'.
                    face.Unknown.Add(Preserve(ref reader, utf8, member, anchor));
                    break;
            }
        }

        return face;
    }

    private static MapMeshSource ReadMeshSource(
        ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, ReadState state)
    {
        var mesh = new MapMeshSource();
        bool sawModel = false;
        int anchor = -1;
        Expect(ref reader, JsonTokenType.StartObject, "'mesh' must be an object", state);

        while (NextMember(ref reader, out string member))
        {
            switch (member)
            {
                case MapFormat.ModelMember:
                    mesh.Model = ReadString(ref reader, member, state);
                    sawModel = true;
                    anchor = 0;
                    break;
                case MapFormat.SubmeshMember:
                    mesh.Submesh = ReadInt(ref reader, member, state);
                    anchor = 1;
                    break;
                default:
                    mesh.Unknown.Add(Preserve(ref reader, utf8, member, anchor));
                    break;
            }
        }

        // Refuse a mesh with no model. Dropping it would look like a node that
        // never had geometry.
        if (!sawModel || string.IsNullOrWhiteSpace(mesh.Model))
            throw Fail(ref reader, $"'mesh' needs a '{MapFormat.ModelMember}' path", state);

        if (mesh.Submesh < 0)
            throw Fail(ref reader, $"'{MapFormat.SubmeshMember}' cannot be negative", state);

        return mesh;
    }

    private static MapLight ReadLight(ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, ReadState state)
    {
        var light = new MapLight();
        int anchor = -1;
        Expect(ref reader, JsonTokenType.StartObject, "'light' must be an object", state);

        while (NextMember(ref reader, out string member))
        {
            switch (member)
            {
                case MapFormat.KindMember:
                    string kind = ReadString(ref reader, member, state);
                    light.Kind = kind switch
                    {
                        MapFormat.DirectionalLight => LightKind.Directional,
                        MapFormat.PointLight => LightKind.Point,
                        MapFormat.SpotLight => LightKind.Spot,
                        MapFormat.RectLight => LightKind.Rect,
                        MapFormat.DiscLight => LightKind.Disc,

                        // No default kind: a typo must not load as directional.
                        _ => throw Fail(ref reader,
                            $"a light '{MapFormat.KindMember}' must be one of "
                            + $"'{MapFormat.DirectionalLight}', '{MapFormat.PointLight}', "
                            + $"'{MapFormat.SpotLight}', '{MapFormat.RectLight}' or "
                            + $"'{MapFormat.DiscLight}', not '{kind}'", state),
                    };
                    anchor = 0;
                    break;
                case MapFormat.ColorMember:
                    light.Color = ReadVector3(ref reader, member, state);
                    anchor = 1;
                    break;
                case MapFormat.IntensityMember:
                    light.Intensity = ReadFloat(ref reader, member, state);
                    anchor = 2;
                    break;
                case MapFormat.RangeMember:
                    light.Range = ReadFloat(ref reader, member, state);
                    anchor = 3;
                    break;
                case MapFormat.EnabledMember:
                    light.Enabled = ReadBool(ref reader, member, state);
                    anchor = 4;
                    break;
                case MapFormat.InnerAngleMember:
                    light.InnerAngle = ReadFloat(ref reader, member, state);
                    anchor = 5;
                    break;
                case MapFormat.OuterAngleMember:
                    light.OuterAngle = ReadFloat(ref reader, member, state);
                    anchor = 6;
                    break;
                case MapFormat.WidthMember:
                    light.Width = ReadFloat(ref reader, member, state);
                    anchor = 7;
                    break;
                case MapFormat.HeightMember:
                    light.Height = ReadFloat(ref reader, member, state);
                    anchor = 8;
                    break;
                case MapFormat.RadiusMember:
                    light.Radius = ReadFloat(ref reader, member, state);
                    anchor = 9;
                    break;
                default:
                    light.Unknown.Add(Preserve(ref reader, utf8, member, anchor));
                    break;
            }
        }

        return light;
    }

    private static MapEntity ReadEntity(
        ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, ReadState state)
    {
        var entity = new MapEntity();
        bool sawClass = false;
        int anchor = -1;
        Expect(ref reader, JsonTokenType.StartObject, "'entity' must be an object", state);

        while (NextMember(ref reader, out string member))
        {
            switch (member)
            {
                case MapFormat.ClassMember:
                    // Not checked against a catalogue: a class this build lacks
                    // must still load and save unchanged.
                    entity.Class = ReadString(ref reader, member, state);
                    sawClass = true;
                    anchor = 0;
                    break;

                case MapFormat.KeysMember:
                    ReadKeys(ref reader, entity.Keys, state);
                    anchor = 1;
                    break;

                case MapFormat.OutputsMember:
                    ReadConnections(ref reader, utf8, entity.Outputs, state);
                    anchor = 2;
                    break;

                default:
                    entity.Unknown.Add(Preserve(ref reader, utf8, member, anchor));
                    break;
            }
        }

        // A missing class is refused. An empty one is accepted: EntityData
        // allows an entity with no class yet, and the writer can produce it.
        if (!sawClass)
            throw Fail(ref reader, $"'entity' needs a '{MapFormat.ClassMember}' name", state);

        return entity;
    }

    private static void ReadKeys(
        ref Utf8JsonReader reader, List<KeyValuePair<string, string>> into, ReadState state)
    {
        Expect(ref reader, JsonTokenType.StartObject, $"'{MapFormat.KeysMember}' must be an object", state);

        while (NextMember(ref reader, out string key))
        {
            // Append, don't merge: a hand-written duplicate key must survive.
            into.Add(new KeyValuePair<string, string>(key, ReadKeyvalue(ref reader, key, state)));
        }
    }

    // Keyvalues are strings on the wire. A bare number is refused, since the
    // writer could not reproduce its spelling.
    private static string ReadKeyvalue(ref Utf8JsonReader reader, string key, ReadState state)
    {
        Read(ref reader, state);
        if (reader.TokenType != JsonTokenType.String)
            throw Fail(ref reader, $"keyvalue '{key}' must be a string; keyvalues are string-typed", state);
        return reader.GetString() ?? string.Empty;
    }

    private static void ReadConnections(
        ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, List<MapConnection> into, ReadState state)
    {
        Expect(ref reader, JsonTokenType.StartArray, $"'{MapFormat.OutputsMember}' must be an array", state);

        while (Read(ref reader, state) && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw Fail(ref reader, "a connection must be an object", state);

            into.Add(ReadConnection(ref reader, utf8, state));
        }
    }

    private static MapConnection ReadConnection(
        ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, ReadState state)
    {
        var connection = new MapConnection();
        int anchor = -1;

        while (NextMember(ref reader, out string member))
        {
            switch (member)
            {
                case MapFormat.OutputMember:
                    connection.Output = ReadString(ref reader, member, state);
                    anchor = 0;
                    break;
                case MapFormat.TargetMember:
                    connection.Target = ReadString(ref reader, member, state);
                    anchor = 1;
                    break;
                case MapFormat.InputMember:
                    connection.Input = ReadString(ref reader, member, state);
                    anchor = 2;
                    break;
                case MapFormat.ParamMember:
                    connection.Param = ReadString(ref reader, member, state);
                    anchor = 3;
                    break;
                case MapFormat.DelayMember:
                    connection.Delay = ReadFloat(ref reader, member, state);
                    anchor = 4;
                    break;
                case MapFormat.TimesMember:
                    connection.Times = ReadInt(ref reader, member, state);
                    anchor = 5;
                    break;
                default:
                    connection.Unknown.Add(Preserve(ref reader, utf8, member, anchor));
                    break;
            }
        }

        return connection;
    }

    // Captures an unknown member's raw value and its anchor in the canonical
    // order. The span starts at the value, not the name, or the writer would
    // emit the name twice.
    private static PreservedMember Preserve(
        ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8, string member, int anchor) =>
        new(member, CanonicalJson.CaptureValue(ref reader, utf8), anchor);

    private static bool NextMember(ref Utf8JsonReader reader, out string member)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.EndObject)
        {
            member = string.Empty;
            return false;
        }
        member = reader.GetString() ?? string.Empty;
        return true;
    }

    private static bool Read(ref Utf8JsonReader reader, ReadState state)
    {
        if (reader.Read()) return true;
        throw Fail(ref reader, "the document ended in the middle of a value", state);
    }

    private static void Expect(
        ref Utf8JsonReader reader, JsonTokenType expected, string what, ReadState state)
    {
        Read(ref reader, state);
        if (reader.TokenType != expected)
            throw Fail(ref reader, what, state);
    }

    private static string ReadString(ref Utf8JsonReader reader, string member, ReadState state)
    {
        Read(ref reader, state);
        if (reader.TokenType != JsonTokenType.String)
            throw Fail(ref reader, $"'{member}' must be a string", state);
        return reader.GetString() ?? string.Empty;
    }

    private static int ReadInt(ref Utf8JsonReader reader, string member, ReadState state)
    {
        Read(ref reader, state);
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int value))
            throw Fail(ref reader, $"'{member}' must be a whole number", state);
        return value;
    }

    private static bool ReadBool(ref Utf8JsonReader reader, string member, ReadState state)
    {
        Read(ref reader, state);
        if (reader.TokenType is not (JsonTokenType.True or JsonTokenType.False))
            throw Fail(ref reader, $"'{member}' must be true or false", state);
        return reader.GetBoolean();
    }

    private static Guid ReadGuid(ref Utf8JsonReader reader, ReadState state)
    {
        Read(ref reader, state);
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out Guid value))
            throw Fail(ref reader, $"'{MapFormat.IdMember}' must be a GUID string", state);
        return value;
    }

    private static float ReadFloat(ref Utf8JsonReader reader, string member, ReadState state)
    {
        Read(ref reader, state);
        if (reader.TokenType != JsonTokenType.Number)
            throw Fail(ref reader, $"'{member}' must be a number", state);
        return reader.GetSingle();
    }

    private static Vector3 ReadVector3(ref Utf8JsonReader reader, string member, ReadState state)
    {
        float[] v = ReadFloats(ref reader, 3, member, state);
        return new Vector3(v[0], v[1], v[2]);
    }

    private static float[] ReadFloats(
        ref Utf8JsonReader reader, int count, string member, ReadState state)
    {
        Expect(ref reader, JsonTokenType.StartArray, $"'{member}' must be an array of {count} numbers", state);
        return ReadFloatsInArray(ref reader, count, $"'{member}'", state);
    }

    // Reads exactly count numbers. The reader is already on StartArray.
    private static float[] ReadFloatsInArray(
        ref Utf8JsonReader reader, int count, string what, ReadState state)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            Read(ref reader, state);
            if (reader.TokenType != JsonTokenType.Number)
                throw Fail(ref reader, $"{what} must be an array of {count} numbers", state);
            values[i] = reader.GetSingle();
        }

        Read(ref reader, state);
        if (reader.TokenType != JsonTokenType.EndArray)
            throw Fail(ref reader, $"{what} must have exactly {count} numbers", state);

        return values;
    }

    private static MapFormatException Fail(ref Utf8JsonReader reader, string message, ReadState state) =>
        new(message, state.NodeName, reader.TokenStartIndex);
}
