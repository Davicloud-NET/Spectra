using SpectraEngine.Core.Assets;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

namespace Spectra.Kitchen.Models;

/// <summary>
/// Fetches an external glTF buffer by content-relative path. Returns null when
/// nothing is there.
/// </summary>
// A delegate so a cook rule can route the read through its context and have
// the sidecar recorded as a dependency.
public delegate byte[]? GltfBufferResolver(string contentPath);

/// <summary>
/// A managed glTF 2.0 and GLB reader for the cook. Node transforms are applied to
/// the vertices, V is flipped, and mirrored parts get their winding reversed.
/// Anything outside the supported set is refused by name.
/// </summary>
// Managed and not Assimp: a native importer's welding and reordering vary by
// version, and cooked bytes must not depend on the machine that cooked them.
public static class GltfReader
{
    /// <summary>The JSON form's extension.</summary>
    public const string GltfExtension = ".gltf";

    /// <summary>The binary container's extension.</summary>
    public const string GlbExtension = ".glb";

    // "glTF" little-endian.
    private const uint GlbMagic = 0x46546C67;
    private const uint GlbJsonChunk = 0x4E4F534A;
    private const uint GlbBinaryChunk = 0x004E4942;
    private const int GlbHeaderSize = 12;
    private const int GlbChunkHeaderSize = 8;

    // Keeps the recursive walk from overflowing the stack on a very long chain.
    private const int MaxNodeDepth = 1024;

    private const int ComponentByte = 5120;
    private const int ComponentUnsignedByte = 5121;
    private const int ComponentShort = 5122;
    private const int ComponentUnsignedShort = 5123;
    private const int ComponentUnsignedInt = 5125;
    private const int ComponentFloat = 5126;

    private const int ModeTriangles = 4;

    private const string Base64Marker = ";base64,";

    /// <summary>Whether <paramref name="contentPath"/> is a file this reader takes.</summary>
    public static bool Handles(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        return contentPath.EndsWith(GltfExtension, StringComparison.OrdinalIgnoreCase)
            || contentPath.EndsWith(GlbExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads one glTF or GLB. Throws <see cref="GltfFormatException"/> for a file
    /// it cannot carry.
    /// </summary>
    /// <param name="source">The file's content path, used in messages and to resolve sibling buffers.</param>
    public static GltfModel Read(ReadOnlySpan<byte> file, string source, GltfBufferResolver resolveBuffer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resolveBuffer);

        SplitContainer(file, source, out ReadOnlySpan<byte> json, out ReadOnlySpan<byte> binaryChunk);

        GltfDocument document = GltfDocument.Parse(json, source);
        RequireSupportedDocument(document, source);

        byte[][] buffers = ResolveBuffers(document, source, binaryChunk, resolveBuffer);
        return Build(document, source, buffers);
    }

    // Decided by the magic, not the extension: a .glb saved as .gltf is common.
    private static void SplitContainer(
        ReadOnlySpan<byte> file, string source, out ReadOnlySpan<byte> json, out ReadOnlySpan<byte> binary)
    {
        json = file;
        binary = default;

        if (file.Length < 4) throw new GltfFormatException($"'{source}' is {file.Length} bytes, too short to be glTF.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(file) != GlbMagic) return;

        if (file.Length < GlbHeaderSize)
        {
            throw new GltfFormatException(
                $"'{source}' starts with the GLB magic and is {file.Length} bytes, too short to hold its " +
                $"{GlbHeaderSize}-byte header.");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(file[4..]);
        if (version != 2)
        {
            throw new GltfFormatException(
                $"'{source}' is GLB container version {version}, and this reader implements version 2. " +
                "Re-export it as glTF 2.0.");
        }

        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(file[8..]);
        if (declared > (uint)file.Length)
        {
            throw new GltfFormatException(
                $"'{source}' declares {declared} bytes and is {file.Length}. It is truncated.");
        }

        bool sawJson = false;
        int at = GlbHeaderSize;
        int end = (int)declared;

        while (at + GlbChunkHeaderSize <= end)
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(file[at..]);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 4)..]);
            int body = at + GlbChunkHeaderSize;

            // Subtract, don't add: body + length can wrap on a corrupt file.
            if (length > (uint)(end - body))
            {
                throw new GltfFormatException(
                    $"'{source}' has a GLB chunk at byte {at} claiming {length} bytes, which runs past the " +
                    $"{end}-byte file.");
            }

            ReadOnlySpan<byte> payload = file.Slice(body, (int)length);
            if (type == GlbJsonChunk && !sawJson)
            {
                json = payload;
                sawJson = true;
            }
            else if (type == GlbBinaryChunk && binary.IsEmpty)
            {
                binary = payload;
            }

            // Chunks are padded to 4 bytes.
            at = body + (int)Align4(length);
        }

        if (!sawJson)
        {
            throw new GltfFormatException(
                $"'{source}' is a GLB with no JSON chunk, so there is no document in it.");
        }
    }

    private static uint Align4(uint value) => (value + 3u) & ~3u;

    private static void RequireSupportedDocument(GltfDocument document, string source)
    {
        if (!document.AssetVersion.StartsWith("2.", StringComparison.Ordinal))
        {
            string stated = document.AssetVersion.Length == 0 ? "nothing" : $"'{document.AssetVersion}'";
            throw new GltfFormatException(
                $"'{source}' states asset version {stated}, and this reader implements glTF 2.0.");
        }

        // A required extension (Draco, quantization) changes how the data reads.
        if (document.ExtensionsRequired.Count > 0)
        {
            throw new GltfFormatException(
                $"'{source}' requires the glTF extension(s) " +
                $"{string.Join(", ", document.ExtensionsRequired)}, which this reader does not implement. " +
                "Re-export without them.");
        }
    }

    private static byte[][] ResolveBuffers(
        GltfDocument document, string source, ReadOnlySpan<byte> binaryChunk, GltfBufferResolver resolveBuffer)
    {
        var buffers = new byte[document.Buffers.Count][];

        for (int i = 0; i < buffers.Length; i++)
        {
            GltfBufferJson buffer = document.Buffers[i];
            byte[] bytes;

            if (buffer.Uri is null)
            {
                // Only buffer 0 of a GLB may omit its uri: it is the BIN chunk.
                if (i != 0 || binaryChunk.IsEmpty)
                {
                    throw new GltfFormatException(
                        $"'{source}' buffer {i} names no uri, which only the GLB binary chunk may do.");
                }

                bytes = binaryChunk.ToArray();
            }
            else if (buffer.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                bytes = DecodeDataUri(buffer.Uri, source, i);
            }
            else
            {
                string path = ResolveSiblingPath(source, buffer.Uri);
                bytes = resolveBuffer(path)
                    ?? throw new GltfFormatException(
                        $"'{source}' buffer {i} names '{buffer.Uri}', which resolves to '{path}' and is not " +
                        "in the content root.");
            }

            if (buffer.ByteLength > 0 && bytes.Length < buffer.ByteLength)
            {
                throw new GltfFormatException(
                    $"'{source}' buffer {i} declares {buffer.ByteLength} bytes and only {bytes.Length} " +
                    "arrived. It is truncated.");
            }

            buffers[i] = bytes;
        }

        return buffers;
    }

    private static byte[] DecodeDataUri(string uri, string source, int index)
    {
        int marker = uri.IndexOf(Base64Marker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            throw new GltfFormatException(
                $"'{source}' buffer {index} is a data uri that is not base64 encoded. Only " +
                "'data:...;base64,' is implemented, because a percent-encoded binary payload is not " +
                "something any exporter writes.");
        }

        try
        {
            return Convert.FromBase64String(uri[(marker + Base64Marker.Length)..]);
        }
        catch (FormatException ex)
        {
            throw new GltfFormatException(
                $"'{source}' buffer {index} carries base64 that does not decode: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Joins a glTF uri against the folder its model sits in, as a normalised
    /// content path. Throws if the uri escapes the content root.
    /// </summary>
    // Resolves ".." itself: ContentRoot.NormalizeRelativePath refuses it, and
    // "../Textures/x.png" is an ordinary export.
    public static string ResolveSiblingPath(string modelContentPath, string uri)
    {
        ArgumentNullException.ThrowIfNull(modelContentPath);
        ArgumentNullException.ThrowIfNull(uri);

        // glTF uris are percent-encoded.
        string decoded = Uri.UnescapeDataString(uri).Replace('\\', '/');

        var segments = new List<string>();
        foreach (string part in modelContentPath.Replace('\\', '/').Split('/'))
            segments.Add(part);

        // Drop the model's file name.
        if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);

        foreach (string part in decoded.Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;

            if (part == "..")
            {
                if (segments.Count == 0)
                {
                    throw new GltfFormatException(
                        $"'{modelContentPath}' names '{uri}', which escapes the content root.");
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(part);
        }

        if (segments.Count == 0)
            throw new GltfFormatException($"'{modelContentPath}' names '{uri}', which is not a path.");

        return ContentRoot.NormalizeRelativePath(string.Join('/', segments));
    }

    private static GltfModel Build(GltfDocument document, string source, byte[][] buffers)
    {
        var submeshes = new List<GltfSubmesh>();
        var dropped = new SortedSet<string>(StringComparer.Ordinal);

        if (document.HasSkins) dropped.Add("skins (SKEL is designed and unwritten in .smodel v1)");
        if (document.HasAnimations) dropped.Add("animations (clips live in their own file, not in a mesh)");

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);

        // Cleared again when a subtree is done, so a node with two parents is
        // emitted twice and only a real cycle is refused.
        var onPath = new bool[document.Nodes.Count];

        foreach (int root in RootNodes(document, source))
            Visit(document, source, buffers, root, Matrix4x4.Identity, 0, onPath, submeshes, dropped);

        for (int i = 0; i < submeshes.Count; i++)
        {
            min = Vector3.Min(min, submeshes[i].BoundsMin);
            max = Vector3.Max(max, submeshes[i].BoundsMax);
        }

        if (submeshes.Count == 0)
        {
            throw new GltfFormatException(
                $"'{source}' holds no drawable triangles: its scene places no mesh, or every mesh it places " +
                "is empty.");
        }

        var materials = new GltfMaterial[document.Materials.Count];
        for (int i = 0; i < materials.Length; i++)
            materials[i] = new GltfMaterial(document.Materials[i].Name, BaseColorUri(document, i));

        return new GltfModel(submeshes, materials, min, max, [.. dropped]);
    }

    private static string? BaseColorUri(GltfDocument document, int material)
    {
        int? texture = document.Materials[material].BaseColorTexture;
        if (texture is not { } index || (uint)index >= (uint)document.TextureSources.Count) return null;

        int image = document.TextureSources[index];
        return (uint)image < (uint)document.ImageUris.Count ? document.ImageUris[image] : null;
    }

    private static IEnumerable<int> RootNodes(GltfDocument document, string source)
    {
        if (document.Scenes.Count > 0)
        {
            int index = document.DefaultScene ?? 0;
            if ((uint)index >= (uint)document.Scenes.Count)
            {
                throw new GltfFormatException(
                    $"'{source}' names default scene {index} and declares {document.Scenes.Count}.");
            }

            return document.Scenes[index];
        }

        // No scene is legal glTF. Treat every node that is nobody's child as a root.
        var claimed = new bool[document.Nodes.Count];
        for (int i = 0; i < document.Nodes.Count; i++)
        {
            foreach (int child in document.Nodes[i].Children)
            {
                if ((uint)child < (uint)claimed.Length) claimed[child] = true;
            }
        }

        var roots = new List<int>();
        for (int i = 0; i < claimed.Length; i++)
        {
            if (!claimed[i]) roots.Add(i);
        }

        return roots;
    }

    private static void Visit(
        GltfDocument document,
        string source,
        byte[][] buffers,
        int index,
        Matrix4x4 parent,
        int depth,
        bool[] onPath,
        List<GltfSubmesh> submeshes,
        SortedSet<string> dropped)
    {
        if ((uint)index >= (uint)document.Nodes.Count)
        {
            throw new GltfFormatException(
                $"'{source}' places node {index} and declares {document.Nodes.Count}.");
        }

        if (onPath[index])
        {
            throw new GltfFormatException(
                $"'{source}' node {index} is its own ancestor. A glTF node hierarchy is a tree, and a walk " +
                "of a cyclic one never ends.");
        }

        if (depth >= MaxNodeDepth)
        {
            throw new GltfFormatException(
                $"'{source}' nests nodes more than {MaxNodeDepth} deep, which no authored hierarchy does.");
        }

        GltfNodeJson node = document.Nodes[index];
        Matrix4x4 world = LocalMatrix(node, source, index) * parent;

        onPath[index] = true;

        if (node.Mesh is { } mesh)
            AddMesh(document, source, buffers, mesh, node, world, submeshes, dropped);

        foreach (int child in node.Children)
            Visit(document, source, buffers, child, world, depth + 1, onPath, submeshes, dropped);

        onPath[index] = false;
    }

    // Row-vector convention. glTF's T * R * S becomes S * R * T. Its matrix is
    // column-major for column vectors, so reading the floats in order into
    // Matrix4x4 is already the transpose needed; do not add one.
    private static Matrix4x4 LocalMatrix(GltfNodeJson node, string source, int index)
    {
        if (node.Matrix is not { } m) return
            Matrix4x4.CreateScale(node.Scale)
            * Matrix4x4.CreateFromQuaternion(node.Rotation)
            * Matrix4x4.CreateTranslation(node.Translation);

        if (node.HasTrs)
        {
            throw new GltfFormatException(
                $"'{source}' node {index} carries both a matrix and a translation, rotation or scale. glTF " +
                "forbids that, and the two disagree about where the node is.");
        }

        return new Matrix4x4(
            m[0], m[1], m[2], m[3],
            m[4], m[5], m[6], m[7],
            m[8], m[9], m[10], m[11],
            m[12], m[13], m[14], m[15]);
    }

    private static void AddMesh(
        GltfDocument document,
        string source,
        byte[][] buffers,
        int meshIndex,
        GltfNodeJson node,
        Matrix4x4 world,
        List<GltfSubmesh> submeshes,
        SortedSet<string> dropped)
    {
        if ((uint)meshIndex >= (uint)document.Meshes.Count)
        {
            throw new GltfFormatException(
                $"'{source}' node '{node.Name}' names mesh {meshIndex} and the file declares " +
                $"{document.Meshes.Count}.");
        }

        GltfMeshJson mesh = document.Meshes[meshIndex];
        for (int i = 0; i < mesh.Primitives.Count; i++)
        {
            GltfPrimitiveJson primitive = mesh.Primitives[i];

            if (primitive.Mode != ModeTriangles)
            {
                throw new GltfFormatException(
                    $"'{source}' mesh '{mesh.Name}' primitive {i} is mode {primitive.Mode} " +
                    $"({DescribeMode(primitive.Mode)}), and this cook writes triangles only. Re-export it " +
                    "triangulated.");
            }

            foreach (string attribute in primitive.OtherAttributes)
                dropped.Add($"vertex attribute {attribute}");

            if (primitive.HasMorphTargets) dropped.Add("morph targets");
            if (primitive.TexCoord0 is null) dropped.Add("texture coordinates (none in the file; written zero)");

            string name = mesh.Name.Length > 0 ? mesh.Name : node.Name;
            submeshes.Add(BuildSubmesh(
                document, source, buffers, primitive, $"{name}[{i}]", world));
        }
    }

    private static GltfSubmesh BuildSubmesh(
        GltfDocument document,
        string source,
        byte[][] buffers,
        GltfPrimitiveJson primitive,
        string name,
        Matrix4x4 world)
    {
        if (primitive.Position is not { } positionAccessor)
        {
            throw new GltfFormatException(
                $"'{source}' primitive '{name}' declares no POSITION attribute, so there is nothing to draw.");
        }

        float[] positions = ReadFloatAccessor(document, source, buffers, positionAccessor, 3, "POSITION");
        int vertexCount = positions.Length / 3;

        float[]? normals = primitive.Normal is { } normalAccessor
            ? ReadFloatAccessor(document, source, buffers, normalAccessor, 3, "NORMAL")
            : null;

        float[]? uvs = primitive.TexCoord0 is { } uvAccessor
            ? ReadFloatAccessor(document, source, buffers, uvAccessor, 2, "TEXCOORD_0")
            : null;

        RequireMatchingCount(source, name, "NORMAL", normals, 3, vertexCount);
        RequireMatchingCount(source, name, "TEXCOORD_0", uvs, 2, vertexCount);

        uint[] indices = primitive.Indices is { } indexAccessor
            ? ReadIndexAccessor(document, source, buffers, indexAccessor, vertexCount, name)
            : Sequential(vertexCount);

        if (indices.Length % 3 != 0)
        {
            throw new GltfFormatException(
                $"'{source}' primitive '{name}' has {indices.Length} indices, which is not a whole number of " +
                "triangles.");
        }

        // A negative determinant mirrors, which flips winding; indices are
        // reversed below or the part renders inside out.
        bool mirrored = world.GetDeterminant() < 0f;
        Matrix4x4 normalMatrix = NormalMatrix(world);

        if (normals is null)
        {
            // glTF spec: no normals means flat shading, so one vertex per corner.
            return FlatShaded(positions, uvs, indices, name, primitive.Material ?? -1, world, mirrored);
        }

        var vertices = new float[vertexCount * 8];
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);

        for (int v = 0; v < vertexCount; v++)
        {
            var position = Vector3.Transform(
                new Vector3(positions[v * 3], positions[(v * 3) + 1], positions[(v * 3) + 2]), world);

            Vector3 normal = TransformNormal(
                new Vector3(normals[v * 3], normals[(v * 3) + 1], normals[(v * 3) + 2]), normalMatrix);

            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);

            int at = v * 8;
            vertices[at] = position.X;
            vertices[at + 1] = position.Y;
            vertices[at + 2] = position.Z;
            vertices[at + 3] = normal.X;
            vertices[at + 4] = normal.Y;
            vertices[at + 5] = normal.Z;
            vertices[at + 6] = uvs is null ? 0f : uvs[v * 2];
            vertices[at + 7] = uvs is null ? 0f : FlipV(uvs[(v * 2) + 1]);
        }

        if (mirrored) ReverseWinding(indices);

        return new GltfSubmesh(name, primitive.Material ?? -1, vertices, indices, min, max);
    }

    private static GltfSubmesh FlatShaded(
        float[] positions,
        float[]? uvs,
        uint[] indices,
        string name,
        int material,
        Matrix4x4 world,
        bool mirrored)
    {
        var vertices = new float[indices.Length * 8];
        var expanded = new uint[indices.Length];
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);

        // Outside the loop: a stackalloc in a loop is not freed per iteration.
        Span<Vector3> corner = stackalloc Vector3[3];

        for (int triangle = 0; triangle < indices.Length; triangle += 3)
        {
            for (int c = 0; c < 3; c++)
            {
                uint index = indices[triangle + c];
                corner[c] = Vector3.Transform(
                    new Vector3(positions[index * 3], positions[(index * 3) + 1], positions[(index * 3) + 2]),
                    world);
            }

            // From the transformed corners.
            Vector3 face = Vector3.Cross(corner[1] - corner[0], corner[2] - corner[0]);
            face = face.LengthSquared() > 0f ? Vector3.Normalize(face) : Vector3.UnitY;
            if (mirrored) face = -face;

            for (int c = 0; c < 3; c++)
            {
                int v = triangle + c;
                uint index = indices[v];

                min = Vector3.Min(min, corner[c]);
                max = Vector3.Max(max, corner[c]);

                int at = v * 8;
                vertices[at] = corner[c].X;
                vertices[at + 1] = corner[c].Y;
                vertices[at + 2] = corner[c].Z;
                vertices[at + 3] = face.X;
                vertices[at + 4] = face.Y;
                vertices[at + 5] = face.Z;
                vertices[at + 6] = uvs is null ? 0f : uvs[index * 2];
                vertices[at + 7] = uvs is null ? 0f : FlipV(uvs[(index * 2) + 1]);
                expanded[v] = (uint)v;
            }
        }

        if (mirrored) ReverseWinding(expanded);

        return new GltfSubmesh(name, material, vertices, expanded, min, max);
    }

    // v = 0 is the bottom of an image here and the top in glTF.
    private static float FlipV(float v) => 1f - v;

    private static void ReverseWinding(uint[] indices)
    {
        for (int i = 0; i + 2 < indices.Length; i += 3)
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
    }

    // Inverse transpose, for non-uniform scale. Falls back to the matrix
    // itself when it is singular.
    private static Matrix4x4 NormalMatrix(Matrix4x4 world) =>
        Matrix4x4.Invert(world, out Matrix4x4 inverse) ? Matrix4x4.Transpose(inverse) : world;

    private static Vector3 TransformNormal(Vector3 normal, Matrix4x4 matrix)
    {
        Vector3 transformed = Vector3.TransformNormal(normal, matrix);
        return transformed.LengthSquared() > 0f ? Vector3.Normalize(transformed) : Vector3.UnitY;
    }

    private static uint[] Sequential(int count)
    {
        var indices = new uint[count];
        for (int i = 0; i < count; i++) indices[i] = (uint)i;
        return indices;
    }

    private static void RequireMatchingCount(
        string source, string name, string attribute, float[]? values, int components, int vertexCount)
    {
        if (values is null || values.Length / components == vertexCount) return;

        throw new GltfFormatException(
            $"'{source}' primitive '{name}' has {vertexCount} positions and {values.Length / components} " +
            $"{attribute} values. glTF requires every attribute of a primitive to have the same count.");
    }

    private static float[] ReadFloatAccessor(
        GltfDocument document, string source, byte[][] buffers, int index, int components, string what)
    {
        GltfAccessorJson accessor = RequireAccessor(document, source, index, what);

        if (accessor.ComponentType != ComponentFloat)
        {
            throw new GltfFormatException(
                $"'{source}' accessor {index} ({what}) has component type " +
                $"{DescribeComponentType(accessor.ComponentType)}, and this cook reads {what} as 32-bit " +
                "float. Re-export without quantization.");
        }

        if (accessor.Normalized)
        {
            throw new GltfFormatException(
                $"'{source}' accessor {index} ({what}) is marked normalized, which is only meaningful for " +
                "integer components and is not implemented here.");
        }

        int declared = ComponentCount(accessor.Type);
        if (declared != components)
        {
            throw new GltfFormatException(
                $"'{source}' accessor {index} ({what}) is {accessor.Type} and this cook needs " +
                $"{components} components.");
        }

        int elementSize = components * sizeof(float);
        LocateAccessor(
            document, source, buffers, accessor, index, elementSize,
            out byte[] buffer, out int start, out int stride);

        var values = new float[accessor.Count * components];
        for (int element = 0; element < accessor.Count; element++)
        {
            ReadOnlySpan<byte> payload = buffer.AsSpan(start + (element * stride), elementSize);
            for (int c = 0; c < components; c++)
            {
                values[(element * components) + c] =
                    BinaryPrimitives.ReadSingleLittleEndian(payload[(c * sizeof(float))..]);
            }
        }

        return values;
    }

    private static uint[] ReadIndexAccessor(
        GltfDocument document, string source, byte[][] buffers, int index, int vertexCount, string name)
    {
        GltfAccessorJson accessor = RequireAccessor(document, source, index, "indices");

        if (ComponentCount(accessor.Type) != 1)
        {
            throw new GltfFormatException(
                $"'{source}' accessor {index} is {accessor.Type} and an index accessor must be SCALAR.");
        }

        int width = accessor.ComponentType switch
        {
            ComponentUnsignedByte => 1,
            ComponentUnsignedShort => 2,
            ComponentUnsignedInt => 4,

            _ => throw new GltfFormatException(
                $"'{source}' accessor {index} has index component type " +
                $"{DescribeComponentType(accessor.ComponentType)}. glTF allows unsigned byte, unsigned " +
                "short and unsigned int."),
        };

        LocateAccessor(
            document, source, buffers, accessor, index, width,
            out byte[] buffer, out int start, out int stride);

        var indices = new uint[accessor.Count];
        for (int element = 0; element < accessor.Count; element++)
        {
            ReadOnlySpan<byte> payload = buffer.AsSpan(start + (element * stride), width);
            indices[element] = width switch
            {
                1 => payload[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(payload),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(payload),
            };
        }

        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] < (uint)vertexCount) continue;

            throw new GltfFormatException(
                $"'{source}' primitive '{name}' index {i} names vertex {indices[i]}, and the primitive has " +
                $"{vertexCount}.");
        }

        return indices;
    }

    private static GltfAccessorJson RequireAccessor(
        GltfDocument document, string source, int index, string what)
    {
        if ((uint)index >= (uint)document.Accessors.Count)
        {
            throw new GltfFormatException(
                $"'{source}' names accessor {index} for {what} and declares {document.Accessors.Count}.");
        }

        GltfAccessorJson accessor = document.Accessors[index];

        if (accessor.Sparse)
        {
            throw new GltfFormatException(
                $"'{source}' accessor {index} ({what}) is sparse, which this cook does not implement. " +
                "Reading only its base array would drop exactly the values a sparse accessor exists to " +
                "carry, so it is refused rather than half read.");
        }

        if (accessor.Count <= 0)
        {
            throw new GltfFormatException(
                $"'{source}' accessor {index} ({what}) declares {accessor.Count} elements.");
        }

        return accessor;
    }

    // Bounds-checks an accessor's whole span before any element is read.
    // Stride is the bufferView's if it states one (interleaved data).
    private static void LocateAccessor(
        GltfDocument document,
        string source,
        byte[][] buffers,
        GltfAccessorJson accessor,
        int accessorIndex,
        int elementSize,
        out byte[] elements,
        out int start,
        out int stride)
    {
        if (accessor.BufferView is not { } viewIndex)
        {
            throw new GltfFormatException(
                $"'{source}' accessor {accessorIndex} names no bufferView. An accessor without one reads as " +
                "zeros, which is only meaningful under a sparse accessor and this cook refuses those.");
        }

        if ((uint)viewIndex >= (uint)document.BufferViews.Count)
        {
            throw new GltfFormatException(
                $"'{source}' accessor {accessorIndex} names bufferView {viewIndex} and the file declares " +
                $"{document.BufferViews.Count}.");
        }

        GltfBufferViewJson view = document.BufferViews[viewIndex];
        if ((uint)view.Buffer >= (uint)buffers.Length)
        {
            throw new GltfFormatException(
                $"'{source}' bufferView {viewIndex} names buffer {view.Buffer} and the file declares " +
                $"{buffers.Length}.");
        }

        byte[] buffer = buffers[view.Buffer];
        if (view.ByteOffset < 0 || view.ByteLength < 0
            || view.ByteOffset > buffer.Length || view.ByteLength > buffer.Length - view.ByteOffset)
        {
            throw new GltfFormatException(
                $"'{source}' bufferView {viewIndex} claims {view.ByteLength} bytes at offset " +
                $"{view.ByteOffset} of a {buffer.Length}-byte buffer.");
        }

        stride = view.ByteStride > 0 ? view.ByteStride : elementSize;
        if (stride < elementSize)
        {
            throw new GltfFormatException(
                $"'{source}' bufferView {viewIndex} states a stride of {stride} bytes for elements that are " +
                $"{elementSize}.");
        }

        long last = (long)accessor.ByteOffset + ((long)(accessor.Count - 1) * stride) + elementSize;
        if (accessor.ByteOffset < 0 || last > view.ByteLength)
        {
            throw new GltfFormatException(
                $"'{source}' accessor {accessorIndex} reads to byte {last} of a {view.ByteLength}-byte " +
                "bufferView.");
        }

        elements = buffer;
        start = view.ByteOffset + accessor.ByteOffset;
    }

    private static int ComponentCount(string type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        _ => -1,
    };

    private static string DescribeComponentType(int componentType) => componentType switch
    {
        ComponentByte => "5120 (BYTE)",
        ComponentUnsignedByte => "5121 (UNSIGNED_BYTE)",
        ComponentShort => "5122 (SHORT)",
        ComponentUnsignedShort => "5123 (UNSIGNED_SHORT)",
        ComponentUnsignedInt => "5125 (UNSIGNED_INT)",
        ComponentFloat => "5126 (FLOAT)",
        _ => $"{componentType} (unknown)",
    };

    private static string DescribeMode(int mode) => mode switch
    {
        0 => "POINTS",
        1 => "LINES",
        2 => "LINE_LOOP",
        3 => "LINE_STRIP",
        4 => "TRIANGLES",
        5 => "TRIANGLE_STRIP",
        6 => "TRIANGLE_FAN",
        _ => "unknown",
    };
}
