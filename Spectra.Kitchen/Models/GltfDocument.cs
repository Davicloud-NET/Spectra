using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace Spectra.Kitchen.Models;

// The glTF 2.0 JSON parsed into flat lists. glTF arrays can appear in any
// order, so references are resolved in a second step, not while parsing.
// Every list is index-aligned with the file's own.
internal sealed class GltfDocument
{
    public string AssetVersion = string.Empty;
    public readonly List<string> ExtensionsRequired = [];
    public readonly List<GltfBufferJson> Buffers = [];
    public readonly List<GltfBufferViewJson> BufferViews = [];
    public readonly List<GltfAccessorJson> Accessors = [];
    public readonly List<GltfMeshJson> Meshes = [];
    public readonly List<GltfNodeJson> Nodes = [];
    public readonly List<int[]> Scenes = [];
    public readonly List<GltfMaterialJson> Materials = [];
    public readonly List<int> TextureSources = [];
    public readonly List<string?> ImageUris = [];
    public int? DefaultScene;
    public bool HasSkins;
    public bool HasAnimations;

    // source only names the file in messages.
    public static GltfDocument Parse(ReadOnlySpan<byte> json, string source)
    {
        var document = new GltfDocument();
        var reader = new Utf8JsonReader(json, new JsonReaderOptions
        {
            // Not legal glTF, but common in hand-edited files.
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                throw new GltfFormatException(
                    $"'{source}' does not begin with a JSON object, so it is not a glTF document.");
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                switch (name)
                {
                    case "asset": document.ReadAsset(ref reader, source); break;
                    case "extensionsRequired": ReadStringArray(ref reader, document.ExtensionsRequired, source); break;
                    case "buffers": document.ReadBuffers(ref reader, source); break;
                    case "bufferViews": document.ReadBufferViews(ref reader, source); break;
                    case "accessors": document.ReadAccessors(ref reader, source); break;
                    case "meshes": document.ReadMeshes(ref reader, source); break;
                    case "nodes": document.ReadNodes(ref reader, source); break;
                    case "scenes": document.ReadScenes(ref reader, source); break;
                    case "scene": document.DefaultScene = ReadInt(ref reader, source, "scene"); break;
                    case "materials": document.ReadMaterials(ref reader, source); break;
                    case "textures": document.ReadTextures(ref reader, source); break;
                    case "images": document.ReadImages(ref reader, source); break;

                    // Presence only, so the cook can report them as dropped.
                    case "skins": document.HasSkins = true; reader.Skip(); break;
                    case "animations": document.HasAnimations = true; reader.Skip(); break;

                    // Cameras, samplers, extras, anything newer.
                    default: reader.Skip(); break;
                }
            }
        }
        catch (JsonException ex)
        {
            throw new GltfFormatException($"'{source}' is not readable JSON: {ex.Message}", ex);
        }

        return document;
    }

    private void ReadAsset(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartObject, source, "asset");

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            reader.Read();

            if (name == "version") AssetVersion = ReadOptionalString(ref reader) ?? string.Empty;
            else reader.Skip();
        }
    }

    private void ReadBuffers(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "buffers");

        while (NextElement(ref reader, source, "buffers"))
        {
            var buffer = new GltfBufferJson();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                switch (name)
                {
                    case "byteLength": buffer.ByteLength = ReadInt(ref reader, source, "buffer.byteLength"); break;
                    case "uri": buffer.Uri = ReadOptionalString(ref reader); break;
                    default: reader.Skip(); break;
                }
            }

            Buffers.Add(buffer);
        }
    }

    private void ReadBufferViews(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "bufferViews");

        while (NextElement(ref reader, source, "bufferViews"))
        {
            var view = new GltfBufferViewJson();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                switch (name)
                {
                    case "buffer": view.Buffer = ReadInt(ref reader, source, "bufferView.buffer"); break;
                    case "byteOffset": view.ByteOffset = ReadInt(ref reader, source, "bufferView.byteOffset"); break;
                    case "byteLength": view.ByteLength = ReadInt(ref reader, source, "bufferView.byteLength"); break;
                    case "byteStride": view.ByteStride = ReadInt(ref reader, source, "bufferView.byteStride"); break;
                    default: reader.Skip(); break;
                }
            }

            BufferViews.Add(view);
        }
    }

    private void ReadAccessors(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "accessors");

        while (NextElement(ref reader, source, "accessors"))
        {
            var accessor = new GltfAccessorJson();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                switch (name)
                {
                    case "bufferView": accessor.BufferView = ReadInt(ref reader, source, "accessor.bufferView"); break;
                    case "byteOffset": accessor.ByteOffset = ReadInt(ref reader, source, "accessor.byteOffset"); break;
                    case "componentType":
                        accessor.ComponentType = ReadInt(ref reader, source, "accessor.componentType");
                        break;
                    case "count": accessor.Count = ReadInt(ref reader, source, "accessor.count"); break;
                    case "type": accessor.Type = ReadOptionalString(ref reader) ?? string.Empty; break;
                    case "normalized": accessor.Normalized = reader.TokenType == JsonTokenType.True; break;

                    // Recorded so the reader can refuse it: ignoring the overrides
                    // would give wrong geometry.
                    case "sparse": accessor.Sparse = true; reader.Skip(); break;

                    default: reader.Skip(); break;
                }
            }

            Accessors.Add(accessor);
        }
    }

    private void ReadMeshes(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "meshes");

        while (NextElement(ref reader, source, "meshes"))
        {
            var mesh = new GltfMeshJson();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                switch (name)
                {
                    case "name": mesh.Name = ReadOptionalString(ref reader) ?? string.Empty; break;
                    case "primitives": ReadPrimitives(ref reader, mesh, source); break;
                    default: reader.Skip(); break;
                }
            }

            Meshes.Add(mesh);
        }
    }

    private static void ReadPrimitives(ref Utf8JsonReader reader, GltfMeshJson mesh, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "primitives");

        while (NextElement(ref reader, source, "primitives"))
        {
            var primitive = new GltfPrimitiveJson();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                switch (name)
                {
                    case "attributes": ReadAttributes(ref reader, primitive, source); break;
                    case "indices": primitive.Indices = ReadInt(ref reader, source, "primitive.indices"); break;
                    case "material": primitive.Material = ReadInt(ref reader, source, "primitive.material"); break;
                    case "mode": primitive.Mode = ReadInt(ref reader, source, "primitive.mode"); break;

                    // Morph targets are dropped, not refused: the base mesh is still valid.
                    case "targets": primitive.HasMorphTargets = true; reader.Skip(); break;

                    default: reader.Skip(); break;
                }
            }

            mesh.Primitives.Add(primitive);
        }
    }

    private static void ReadAttributes(ref Utf8JsonReader reader, GltfPrimitiveJson primitive, string source)
    {
        Expect(ref reader, JsonTokenType.StartObject, source, "primitive.attributes");

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            reader.Read();

            int accessor = ReadInt(ref reader, source, $"attribute {name}");
            switch (name)
            {
                case "POSITION": primitive.Position = accessor; break;
                case "NORMAL": primitive.Normal = accessor; break;
                case "TEXCOORD_0": primitive.TexCoord0 = accessor; break;
                default: primitive.OtherAttributes.Add(name); break;
            }
        }
    }

    private void ReadNodes(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "nodes");

        while (NextElement(ref reader, source, "nodes"))
        {
            var node = new GltfNodeJson();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                switch (name)
                {
                    case "name": node.Name = ReadOptionalString(ref reader) ?? string.Empty; break;
                    case "mesh": node.Mesh = ReadInt(ref reader, source, "node.mesh"); break;
                    case "children": node.Children = ReadIntArray(ref reader, source, "node.children"); break;
                    case "matrix": node.Matrix = ReadFloats(ref reader, source, "node.matrix", 16); break;

                    case "translation":
                        node.Translation = ToVector(ReadFloats(ref reader, source, "node.translation", 3));
                        node.HasTrs = true;
                        break;

                    case "rotation":
                        float[] q = ReadFloats(ref reader, source, "node.rotation", 4);
                        node.Rotation = new Quaternion(q[0], q[1], q[2], q[3]);
                        node.HasTrs = true;
                        break;

                    case "scale":
                        node.Scale = ToVector(ReadFloats(ref reader, source, "node.scale", 3));
                        node.HasTrs = true;
                        break;

                    default: reader.Skip(); break;
                }
            }

            Nodes.Add(node);
        }
    }

    private void ReadScenes(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "scenes");

        while (NextElement(ref reader, source, "scenes"))
        {
            int[] roots = [];
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                if (name == "nodes") roots = ReadIntArray(ref reader, source, "scene.nodes");
                else reader.Skip();
            }

            Scenes.Add(roots);
        }
    }

    private void ReadMaterials(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "materials");

        while (NextElement(ref reader, source, "materials"))
        {
            var material = new GltfMaterialJson();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                switch (name)
                {
                    case "name": material.Name = ReadOptionalString(ref reader) ?? string.Empty; break;
                    case "pbrMetallicRoughness":
                        material.BaseColorTexture = ReadBaseColorTexture(ref reader, source);
                        break;
                    default: reader.Skip(); break;
                }
            }

            Materials.Add(material);
        }
    }

    private static int? ReadBaseColorTexture(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartObject, source, "pbrMetallicRoughness");

        int? texture = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            reader.Read();

            if (name != "baseColorTexture")
            {
                reader.Skip();
                continue;
            }

            Expect(ref reader, JsonTokenType.StartObject, source, "baseColorTexture");
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string field = reader.GetString()!;
                reader.Read();

                if (field == "index") texture = ReadInt(ref reader, source, "baseColorTexture.index");
                else reader.Skip();
            }
        }

        return texture;
    }

    private void ReadTextures(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "textures");

        while (NextElement(ref reader, source, "textures"))
        {
            int image = -1;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                if (name == "source") image = ReadInt(ref reader, source, "texture.source");
                else reader.Skip();
            }

            TextureSources.Add(image);
        }
    }

    private void ReadImages(ref Utf8JsonReader reader, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "images");

        while (NextElement(ref reader, source, "images"))
        {
            string? uri = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();

                if (name == "uri") uri = ReadOptionalString(ref reader);
                else reader.Skip();
            }

            // Null uri: the image is embedded in a bufferView, which is not resolved.
            ImageUris.Add(uri);
        }
    }

    // Steps to the next element of the array the reader is on. False at its end.
    private static bool NextElement(ref Utf8JsonReader reader, string source, string what)
    {
        if (!reader.Read())
            throw new GltfFormatException($"'{source}' ends inside {what}.");

        if (reader.TokenType == JsonTokenType.EndArray) return false;

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new GltfFormatException(
                $"'{source}' has a {reader.TokenType} in {what}, where glTF requires an object.");
        }

        return true;
    }

    private static void ReadStringArray(ref Utf8JsonReader reader, List<string> into, string source)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, "a string array");

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new GltfFormatException(
                    $"'{source}' has a {reader.TokenType} where a string was expected.");
            }

            into.Add(reader.GetString()!);
        }
    }

    private static int[] ReadIntArray(ref Utf8JsonReader reader, string source, string what)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, what);

        var values = new List<int>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            values.Add(ReadInt(ref reader, source, what));

        return [.. values];
    }

    private static float[] ReadFloats(ref Utf8JsonReader reader, string source, string what, int expected)
    {
        Expect(ref reader, JsonTokenType.StartArray, source, what);

        var values = new List<float>(expected);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetSingle(out float value))
            {
                throw new GltfFormatException(
                    $"'{source}' has a {reader.TokenType} in {what} where a number was expected.");
            }

            values.Add(value);
        }

        if (values.Count != expected)
        {
            throw new GltfFormatException(
                $"'{source}' declares {what} with {values.Count} numbers; glTF requires {expected}.");
        }

        return [.. values];
    }

    private static int ReadInt(ref Utf8JsonReader reader, string source, string what)
    {
        // glTF says integer. 1.0 is refused, not truncated.
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int value))
        {
            throw new GltfFormatException(
                $"'{source}' has a {reader.TokenType} for {what}, where glTF requires an integer.");
        }

        return value;
    }

    // GetString throws InvalidOperationException on a non-string token; callers
    // expect GltfFormatException only.
    private static string? ReadOptionalString(ref Utf8JsonReader reader) =>
        reader.TokenType is JsonTokenType.String or JsonTokenType.Null ? reader.GetString() : null;

    private static void Expect(ref Utf8JsonReader reader, JsonTokenType token, string source, string what)
    {
        if (reader.TokenType == token) return;

        throw new GltfFormatException(
            $"'{source}' has {reader.TokenType} where {what} should be a {token}.");
    }

    private static Vector3 ToVector(float[] values) => new(values[0], values[1], values[2]);
}

internal sealed class GltfBufferJson
{
    public int ByteLength;
    public string? Uri;
}

internal sealed class GltfBufferViewJson
{
    public int Buffer;
    public int ByteOffset;
    public int ByteLength;
    public int ByteStride;
}

internal sealed class GltfAccessorJson
{
    public int? BufferView;
    public int ByteOffset;
    public int ComponentType;
    public int Count;
    public string Type = string.Empty;
    public bool Normalized;
    public bool Sparse;
}

internal sealed class GltfPrimitiveJson
{
    public int? Position;
    public int? Normal;
    public int? TexCoord0;
    public int? Indices;
    public int? Material;

    // 4 is TRIANGLES, the glTF default when the member is omitted.
    public int Mode = 4;

    public bool HasMorphTargets;
    public readonly List<string> OtherAttributes = [];
}

internal sealed class GltfMeshJson
{
    public string Name = string.Empty;
    public readonly List<GltfPrimitiveJson> Primitives = [];
}

internal sealed class GltfNodeJson
{
    public string Name = string.Empty;
    public int? Mesh;
    public int[] Children = [];
    public float[]? Matrix;
    public Vector3 Translation = Vector3.Zero;
    public Quaternion Rotation = Quaternion.Identity;
    public Vector3 Scale = Vector3.One;

    // glTF forbids a node with both a matrix and TRS; the reader refuses one.
    public bool HasTrs;
}

internal sealed class GltfMaterialJson
{
    public string Name = string.Empty;
    public int? BaseColorTexture;
}
