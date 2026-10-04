using Silk.NET.Assimp;
using SpectraEngine.Core.Bsp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
// Silk.NET.Assimp's File/Material/Mesh/Node/Scene collide with engine names.
using AiMaterial = Silk.NET.Assimp.Material;
using AiMesh = Silk.NET.Assimp.Mesh;
using AiNode = Silk.NET.Assimp.Node;
using AiScene = Silk.NET.Assimp.Scene;
using SysFile = System.IO.File;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// Reads a model file from disk through Assimp and converts it into
/// <see cref="ModelData"/>. Callable from any thread. Survivable content
/// problems become <see cref="ModelData.Warnings"/>; only an unusable file throws.
/// </summary>
public static class ModelImporter
{
    // Bits 0-3 of aiMesh::mPrimitiveTypes are the primitive kinds; bit 4 is
    // the n-gon hint.
    private const uint PrimitiveKindMask = 0xF;
    private const uint TrianglePrimitiveBit = (uint)PrimitiveType.Triangle;

    // The node walk is recursive, so cap the depth.
    private const int MaxNodeDepth = 256;

    private const int MaxWarnings = 64;

    // Never disposed: the native handle is needed for the process's life.
    private static readonly Lazy<Assimp> Api = new(
        () =>
        {
            SilkPlatform.UsePortableRuntimeId();
            return Assimp.GetApi();
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Imports the model at <paramref name="absolutePath"/>.</summary>
    /// <param name="absolutePath">Absolute path of the model file.</param>
    /// <param name="contentRootPath">
    /// Absolute content root. Texture references are made relative to it.
    /// </param>
    /// <param name="options">Null uses <see cref="ModelImportOptions.Default"/>.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">
    /// The file is not a readable model, or holds no triangle geometry.
    /// </exception>
    public static unsafe ModelData Import(
        string absolutePath,
        string contentRootPath,
        ModelImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);
        ArgumentNullException.ThrowIfNull(contentRootPath);
        options ??= ModelImportOptions.Default;

        if (!SysFile.Exists(absolutePath))
            throw new FileNotFoundException($"Model '{absolutePath}' does not exist.", absolutePath);

        Assimp ai = Api.Value;
        var warnings = new List<string>();

        AiScene* scene = ai.ImportFile(absolutePath, options.BuildPostProcessFlags());
        if (scene is null)
        {
            throw new InvalidDataException(
                $"Could not import model '{absolutePath}': {ai.GetErrorStringS()}");
        }

        try
        {
            if (scene->MRootNode is null)
                throw new InvalidDataException($"Model '{absolutePath}' has no scene hierarchy.");

            if ((scene->MFlags & Assimp.SceneFlagsIncomplete) != 0)
                Warn(warnings, "the importer reported the scene as incomplete");

            (ModelMesh[] meshes, int[] remap) = ConvertMeshes(scene, warnings);
            if (meshes.Length == 0)
            {
                throw new InvalidDataException(
                    $"Model '{absolutePath}' contains no triangle geometry.");
            }

            ModelMaterial[] materials = ConvertMaterials(
                ai, scene, Path.GetDirectoryName(absolutePath) ?? string.Empty,
                Path.GetFullPath(contentRootPath), warnings);

            ModelNode root = ConvertNode(scene->MRootNode, remap, warnings, depth: 0);
            Aabb bounds = ComputeBounds(root, meshes);

            return new ModelData(
                absolutePath, meshes, materials, root, bounds, warnings.ToArray());
        }
        finally
        {
            ai.ReleaseImport(scene);
        }
    }

    // Remap is source index -> converted index, -1 for a skipped mesh, so the
    // node walk can drop references to those.
    private static unsafe (ModelMesh[] Meshes, int[] Remap) ConvertMeshes(
        AiScene* scene, List<string> warnings)
    {
        uint sourceCount = scene->MNumMeshes;
        var remap = new int[sourceCount];
        var meshes = new List<ModelMesh>((int)sourceCount);

        for (uint i = 0; i < sourceCount; i++)
        {
            remap[i] = -1;
            AiMesh* mesh = scene->MMeshes[i];
            if (mesh is null) continue;

            // SortByPrimitiveType already split mixed meshes, so anything that
            // is not all triangles here is points or lines.
            if ((mesh->MPrimitiveTypes & PrimitiveKindMask) != TrianglePrimitiveBit)
            {
                Warn(warnings,
                    $"mesh '{NameOf(mesh->MName)}' is not triangle geometry and was skipped");
                continue;
            }

            if (mesh->MNumVertices == 0 || mesh->MNumFaces == 0 || mesh->MVertices is null)
                continue;

            remap[i] = meshes.Count;
            meshes.Add(ConvertMesh(mesh, warnings));
        }

        return (meshes.ToArray(), remap);
    }

    private static unsafe ModelMesh ConvertMesh(AiMesh* mesh, List<string> warnings)
    {
        int vertexCount = (int)mesh->MNumVertices;
        bool hasNormals = mesh->MNormals is not null;
        // Channel 0 only: the standard layout has one uv.
        Vector3* uvs = mesh->MTextureCoords[0];
        bool hasUvs = uvs is not null;

        var vertices = new float[vertexCount * ModelVertexLayout.FloatsPerVertex];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        for (int v = 0; v < vertexCount; v++)
        {
            int b = v * ModelVertexLayout.FloatsPerVertex;
            Vector3 position = mesh->MVertices[v];
            vertices[b + 0] = position.X;
            vertices[b + 1] = position.Y;
            vertices[b + 2] = position.Z;

            // Fallback only reached when GenerateMissingNormals is off.
            Vector3 normal = hasNormals ? mesh->MNormals[v] : Vector3.UnitY;
            vertices[b + 3] = normal.X;
            vertices[b + 4] = normal.Y;
            vertices[b + 5] = normal.Z;

            // Missing uvs stay (0,0).
            if (hasUvs)
            {
                Vector3 uv = uvs[v];
                vertices[b + 6] = uv.X;
                vertices[b + 7] = uv.Y;
            }

            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        uint faceCount = mesh->MNumFaces;
        var indices = new List<uint>((int)faceCount * 3);
        for (uint f = 0; f < faceCount; f++)
        {
            Face face = mesh->MFaces[f];
            // Triangulate should guarantee this. Drop anything else.
            if (face.MNumIndices != 3 || face.MIndices is null) continue;
            indices.Add(face.MIndices[0]);
            indices.Add(face.MIndices[1]);
            indices.Add(face.MIndices[2]);
        }

        string name = NameOf(mesh->MName);
        if (!hasUvs)
            Warn(warnings, $"mesh '{name}' has no texture coordinates; uvs are zeroed");

        return new ModelMesh(
            name,
            (int)mesh->MMaterialIndex,
            vertices,
            indices.ToArray(),
            new Aabb(min, max),
            hasNormals,
            hasUvs);
    }

    private static unsafe ModelMaterial[] ConvertMaterials(
        Assimp ai, AiScene* scene, string modelDirectory, string contentRoot, List<string> warnings)
    {
        var materials = new ModelMaterial[scene->MNumMaterials];
        for (uint i = 0; i < scene->MNumMaterials; i++)
        {
            AiMaterial* material = scene->MMaterials[i];
            string name = ReadMaterialName(ai, material, i);
            string? texture = ReadDiffuseTexture(
                ai, material, name, modelDirectory, contentRoot, warnings);
            materials[i] = new ModelMaterial(name, texture, ReadBaseColor(ai, material));
        }
        return materials;
    }

    private static unsafe string ReadMaterialName(Assimp ai, AiMaterial* material, uint index)
    {
        AssimpString name = default;
        if (ai.GetMaterialString(material, Assimp.MaterialNameBase, 0, 0, &name) == Return.Success &&
            name.Length > 0)
        {
            return name.AsString;
        }

        // Unnamed materials get a stable name so overrides can address them.
        return $"material{index}";
    }

    private static unsafe Vector3 ReadBaseColor(Assimp ai, AiMaterial* material)
    {
        Span<float> rgba = stackalloc float[4];
        rgba.Clear();
        uint count = 4;
        fixed (float* p = rgba)
        {
            if (ai.GetMaterialFloatArray(
                    material, Assimp.MaterialColorDiffuseBase, 0, 0, p, &count) != Return.Success ||
                count < 3)
            {
                // White leaves the texture untinted.
                return Vector3.One;
            }
        }
        return new Vector3(rgba[0], rgba[1], rgba[2]);
    }

    // Classic formats write Diffuse, glTF writes BaseColor.
    private static unsafe string? ReadDiffuseTexture(
        Assimp ai,
        AiMaterial* material,
        string materialName,
        string modelDirectory,
        string contentRoot,
        List<string> warnings)
    {
        if (!TryReadTexturePath(ai, material, TextureType.Diffuse, out string raw) &&
            !TryReadTexturePath(ai, material, TextureType.BaseColor, out raw))
        {
            return null;
        }

        return ResolveTexturePath(raw, materialName, modelDirectory, contentRoot, warnings);
    }

    private static unsafe bool TryReadTexturePath(
        Assimp ai, AiMaterial* material, TextureType type, out string path)
    {
        path = string.Empty;
        if (ai.GetMaterialTextureCount(material, type) == 0) return false;

        AssimpString value = default;
        if (ai.GetMaterialString(
                material, Assimp.MaterialTextureBase, (uint)type, 0, &value) != Return.Success)
        {
            return false;
        }

        path = value.AsString;
        return path.Length > 0;
    }

    // Tries the path as written (relative to the model file), then the bare
    // file name under Textures/. Exported models often carry the authoring
    // machine's absolute paths.
    private static string? ResolveTexturePath(
        string raw, string materialName, string modelDirectory, string contentRoot, List<string> warnings)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;

        if (trimmed.StartsWith(Assimp.EmbeddedTexnamePrefix, StringComparison.Ordinal))
        {
            Warn(warnings,
                $"material '{materialName}' uses an embedded texture ('{trimmed}'), which is not supported");
            return null;
        }

        if (TryMakeContentRelative(
                CombineSafe(modelDirectory, trimmed), contentRoot, out string? asWritten))
        {
            return asWritten;
        }

        string fileName = Path.GetFileName(trimmed.Replace('\\', '/'));
        if (fileName.Length > 0 &&
            TryMakeContentRelative(
                Path.Combine(contentRoot, "Textures", fileName), contentRoot, out string? relocated))
        {
            Warn(warnings,
                $"material '{materialName}': texture '{trimmed}' was not found where the model points; " +
                $"using '{relocated}'");
            return relocated;
        }

        Warn(warnings,
            $"material '{materialName}': texture '{trimmed}' is not under the content root; " +
            "the material will fall back to the engine default");
        return null;
    }

    // Path.Combine returns a rooted `relative` unchanged, so absolute paths work.
    private static string CombineSafe(string directory, string relative)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(directory, relative));
        }
        catch (ArgumentException)
        {
            // Invalid characters in the file's path.
            return string.Empty;
        }
    }

    private static bool TryMakeContentRelative(
        string absolutePath, string contentRoot, out string? relativePath)
    {
        relativePath = null;
        if (absolutePath.Length == 0 || !SysFile.Exists(absolutePath)) return false;

        string relative = Path.GetRelativePath(contentRoot, absolutePath);
        // Rooted means another volume, ".." means above the root.
        if (Path.IsPathRooted(relative) ||
            relative.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        relativePath = ContentRoot.NormalizeRelativePath(relative);
        return true;
    }

    private static unsafe ModelNode ConvertNode(
        AiNode* node, int[] meshRemap, List<string> warnings, int depth)
    {
        string name = NameOf(node->MName);

        // Assimp is column-vector, System.Numerics is row-vector.
        Matrix4x4 local = Matrix4x4.Transpose(node->MTransformation);

        bool exact = Matrix4x4.Decompose(
            local, out Vector3 scale, out Quaternion rotation, out Vector3 position);
        if (!exact)
        {
            Warn(warnings,
                $"node '{name}' has a transform that is not a position/rotation/scale " +
                "(sheared or mirrored); instantiation will place it at the identity");
            scale = Vector3.One;
            rotation = Quaternion.Identity;
            position = Vector3.Zero;
        }

        int[] meshIndices = [];
        if (node->MNumMeshes > 0)
        {
            var kept = new List<int>((int)node->MNumMeshes);
            for (uint i = 0; i < node->MNumMeshes; i++)
            {
                uint source = node->MMeshes[i];
                if (source < meshRemap.Length && meshRemap[source] >= 0)
                    kept.Add(meshRemap[source]);
            }
            meshIndices = kept.ToArray();
        }

        ModelNode[] children = [];
        if (node->MNumChildren > 0)
        {
            if (depth >= MaxNodeDepth)
            {
                Warn(warnings,
                    $"node '{name}' is deeper than {MaxNodeDepth} levels; its children were dropped");
            }
            else
            {
                children = new ModelNode[node->MNumChildren];
                for (uint i = 0; i < node->MNumChildren; i++)
                    children[i] = ConvertNode(node->MChildren[i], meshRemap, warnings, depth + 1);
            }
        }

        return new ModelNode(name, local, position, rotation, scale, exact, meshIndices, children);
    }

    private static Aabb ComputeBounds(ModelNode root, ModelMesh[] meshes)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;

        var stack = new Stack<(ModelNode Node, Matrix4x4 World)>();
        stack.Push((root, root.LocalMatrix));
        while (stack.Count > 0)
        {
            (ModelNode node, Matrix4x4 world) = stack.Pop();

            IReadOnlyList<int> indices = node.MeshIndices;
            for (int i = 0; i < indices.Count; i++)
            {
                Aabb box = meshes[indices[i]].LocalBounds.Transform(world);
                min = Vector3.Min(min, box.Min);
                max = Vector3.Max(max, box.Max);
                any = true;
            }

            IReadOnlyList<ModelNode> children = node.Children;
            for (int i = 0; i < children.Count; i++)
                stack.Push((children[i], children[i].LocalMatrix * world));
        }

        return any ? new Aabb(min, max) : new Aabb(Vector3.Zero, Vector3.Zero);
    }

    private static string NameOf(AssimpString value) =>
        value.Length > 0 ? value.AsString : string.Empty;

    private static void Warn(List<string> warnings, string message)
    {
        if (warnings.Count < MaxWarnings)
            warnings.Add(message);
        else if (warnings.Count == MaxWarnings)
            warnings.Add($"further warnings suppressed after {MaxWarnings}");
    }
}
