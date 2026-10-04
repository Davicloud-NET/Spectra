using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Spectra.Kitchen.Tests;

// Hand-written glTF 2.0 documents, one triangle each, with a parameter per thing
// the reader should refuse. Written from the glTF spec, not through GltfReader,
// so the reader is not checked against its own code.
internal static class GltfFixture
{
    public const string MaterialName = "FixtureSurface";

    // Asymmetric on every axis, so a component swap changes a number.
    private static readonly float[] Positions = [0f, 0f, 0f, 2f, 0f, 0.5f, 0f, 3f, 1.5f];

    // Not the face normal: the reader must carry the file's, not recompute.
    private static readonly float[] Normals = [0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f];

    // v away from 0 and 1, so a flip and a swap give different numbers.
    private static readonly float[] Uvs = [0.25f, 0.75f, 1f, 0.75f, 0.25f, 0.125f];

    private static readonly ushort[] Indices = [0, 1, 2];

    public static byte[] Buffer()
    {
        var bytes = new byte[BufferLength];
        int at = 0;

        foreach (float value in Positions)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), value);
            at += 4;
        }

        foreach (float value in Normals)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), value);
            at += 4;
        }

        foreach (float value in Uvs)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), value);
            at += 4;
        }

        foreach (ushort value in Indices)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
            at += 2;
        }

        return bytes;
    }

    public static ReadOnlySpan<float> ExpectedPositions => Positions;

    // As written in the file, before the engine's v flip.
    public static ReadOnlySpan<float> AuthoredUvs => Uvs;

    private const int PositionOffset = 0;
    private const int NormalOffset = 36;
    private const int UvOffset = 72;
    private const int IndexOffset = 96;
    private const int BufferLength = 102;

    // By default the buffer is inline as a data uri.
    public static string Json(
        string materialName = MaterialName,
        int mode = 4,
        bool sparsePositions = false,
        bool omitNormals = false,
        bool omitIndices = false,
        string? requiredExtension = null,
        string? assetVersion = "2.0",
        string? bufferUri = null,
        float[]? nodeTranslation = null,
        float[]? nodeMatrix = null,
        int indexComponentType = 5123,
        string? extraNodeAttribute = null)
    {
        string uri = bufferUri ?? DataUri(Buffer());

        var attributes = new List<string> { "\"POSITION\": 0" };
        if (!omitNormals) attributes.Add("\"NORMAL\": 1");
        attributes.Add("\"TEXCOORD_0\": 2");
        if (extraNodeAttribute is not null) attributes.Add($"\"{extraNodeAttribute}\": 2");

        string primitive =
            $"{{ \"attributes\": {{ {string.Join(", ", attributes)} }}, " +
            (omitIndices ? string.Empty : "\"indices\": 3, ") +
            $"\"material\": 0, \"mode\": {mode} }}";

        string placement = nodeMatrix is not null
            ? $", \"matrix\": [{Numbers(nodeMatrix)}]"
            : nodeTranslation is not null
                ? $", \"translation\": [{Numbers(nodeTranslation)}]"
                : string.Empty;

        string sparse = sparsePositions
            ? ", \"sparse\": { \"count\": 1, \"indices\": { \"bufferView\": 3, \"componentType\": 5123 }, " +
              "\"values\": { \"bufferView\": 0 } }"
            : string.Empty;

        var json = new StringBuilder();
        json.Append('{');
        if (assetVersion is not null)
            json.Append($"\"asset\": {{ \"version\": \"{assetVersion}\" }},");

        if (requiredExtension is not null)
            json.Append($"\"extensionsRequired\": [\"{requiredExtension}\"],");

        json.Append("\"scene\": 0,");
        json.Append("\"scenes\": [ { \"nodes\": [0] } ],");
        json.Append($"\"nodes\": [ {{ \"name\": \"FixtureNode\", \"mesh\": 0{placement} }} ],");
        json.Append($"\"meshes\": [ {{ \"name\": \"FixtureMesh\", \"primitives\": [ {primitive} ] }} ],");
        json.Append(
            $"\"materials\": [ {{ \"name\": \"{materialName}\", \"pbrMetallicRoughness\": " +
            "{ \"baseColorTexture\": { \"index\": 0 } } } ],");
        json.Append("\"textures\": [ { \"source\": 0 } ],");
        json.Append("\"images\": [ { \"uri\": \"../Textures/fixture.png\" } ],");
        json.Append("\"accessors\": [");
        json.Append(
            $"{{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"{sparse}}},");
        json.Append("{\"bufferView\":1,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"},");
        json.Append("{\"bufferView\":2,\"componentType\":5126,\"count\":3,\"type\":\"VEC2\"},");
        json.Append(
            $"{{\"bufferView\":3,\"componentType\":{indexComponentType},\"count\":3,\"type\":\"SCALAR\"}}");
        json.Append("],");
        json.Append("\"bufferViews\": [");
        json.Append($"{{\"buffer\":0,\"byteOffset\":{PositionOffset},\"byteLength\":36}},");
        json.Append($"{{\"buffer\":0,\"byteOffset\":{NormalOffset},\"byteLength\":36}},");
        json.Append($"{{\"buffer\":0,\"byteOffset\":{UvOffset},\"byteLength\":24}},");
        json.Append($"{{\"buffer\":0,\"byteOffset\":{IndexOffset},\"byteLength\":6}}");
        json.Append("],");
        json.Append(
            uri.Length == 0
                ? $"\"buffers\": [ {{ \"byteLength\": {BufferLength} }} ]"
                : $"\"buffers\": [ {{ \"byteLength\": {BufferLength}, \"uri\": \"{uri}\" }} ]");
        json.Append('}');
        return json.ToString();
    }

    // No buffer uri: the buffer is the GLB binary chunk.
    public static string GlbJson() => Json(bufferUri: string.Empty);

    // Wraps a document and its buffer in a GLB. Chunks are 4-byte aligned.
    public static byte[] Glb(string json, byte[] binary, uint version = 2, int declaredLengthDelta = 0)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        int jsonPadded = Align4(jsonBytes.Length);
        int binaryPadded = Align4(binary.Length);

        int total = 12 + 8 + jsonPadded + (binary.Length > 0 ? 8 + binaryPadded : 0);
        var file = new byte[total];

        BinaryPrimitives.WriteUInt32LittleEndian(file, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), (uint)(total + declaredLengthDelta));

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), (uint)jsonPadded);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), 0x4E4F534A);
        jsonBytes.CopyTo(file, 20);

        // Spec: JSON pads with spaces, BIN with zeros.
        for (int i = 20 + jsonBytes.Length; i < 20 + jsonPadded; i++) file[i] = 0x20;

        if (binary.Length > 0)
        {
            int at = 20 + jsonPadded;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), (uint)binaryPadded);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), 0x004E4942);
            binary.CopyTo(file, at + 8);
        }

        return file;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static string DataUri(byte[] bytes) =>
        "data:application/octet-stream;base64," + Convert.ToBase64String(bytes);

    // Invariant culture: JSON needs a dot.
    private static string Numbers(float[] values) =>
        string.Join(", ", Array.ConvertAll(values, v => v.ToString("R", CultureInfo.InvariantCulture)));
}
