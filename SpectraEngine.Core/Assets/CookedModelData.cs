using SpectraEngine.Core.Assets.Models;
using SpectraEngine.Core.Bsp;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// Turns a validated <c>.smodel</c> into the <see cref="ModelData"/> the rest of
/// the engine already knows how to upload, instantiate and draw.
/// </summary>
// Copies VBUF and IBUF once, so the mapped input can be closed afterwards.
public static class CookedModelData
{
    /// <summary>
    /// Builds the CPU model. Pure and thread-safe: no GPU, no filesystem.
    /// </summary>
    /// <param name="model">The validated file.</param>
    /// <param name="relativePath">
    /// The authored content path the caller asked for, which becomes
    /// <see cref="ModelData.SourcePath"/>.
    /// </param>
    /// <exception cref="SmodelFormatException">
    /// The file declares a vertex layout this build cannot upload.
    /// </exception>
    public static ModelData Build(in SmodelModel model, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        RequireStandardLayout(model);

        int submeshCount = model.Submeshes.Length;
        var meshes = new ModelMesh[submeshCount];
        var indices = new uint[model.IndexCount];
        for (int i = 0; i < indices.Length; i++) indices[i] = model.IndexAt(i);
        var geometry = new ModelGeometry(model.Vertices.ToArray(), indices);
        var materials = new List<ModelMaterial>(submeshCount);

        // Path -> slot, so submeshes sharing a material share a slot.
        var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < submeshCount; i++)
        {
            SmodelSubmesh submesh = model.Submeshes[i];
            meshes[i] = BuildMesh(model, geometry, submesh, i, MaterialSlot(model, submesh, materials, slots));
        }

        // Always at least one slot, so a submesh's material index never dangles.
        if (materials.Count == 0) materials.Add(new ModelMaterial(string.Empty, null, Vector3.One));

        // One node for every submesh: the cook baked the node transforms into
        // the vertices, so a .smodel has no hierarchy.
        var root = new ModelNode(
            NameWithoutExtension(relativePath),
            Matrix4x4.Identity,
            Vector3.Zero,
            Quaternion.Identity,
            Vector3.One,
            transformIsExact: true,
            MeshIndices(submeshCount),
            []);

        return new ModelData(
            relativePath,
            meshes,
            [.. materials],
            root,
            new Aabb(model.BoundsMin, model.BoundsMax),
            []);
    }

    private static void RequireStandardLayout(in SmodelModel model)
    {
        if (model.VertexLayoutId == SmodelStandardLayout.LayoutId
            && model.VertexStrideFloats == SmodelStandardLayout.StrideFloats)
        {
            return;
        }

        // No conversion path for other layouts in this build.
        throw new SmodelFormatException(
            $"'{model.Source}' declares vertex layout 0x{model.VertexLayoutId:X8} at " +
            $"{model.VertexStrideFloats} floats per vertex, and this build uploads only the standard " +
            $"layout 0x{SmodelStandardLayout.LayoutId:X8} at {SmodelStandardLayout.StrideFloats}. Recook " +
            "the model.");
    }

    private static int MaterialSlot(
        in SmodelModel model,
        in SmodelSubmesh submesh,
        List<ModelMaterial> materials,
        Dictionary<string, int> slots)
    {
        if (!submesh.HasMaterial)
        {
            // One shared slot that resolves to the default material.
            return Slot(string.Empty, null, materials, slots);
        }

        string path = model.GetName(submesh.MaterialNameOffset);
        return Slot(path, path, materials, slots);
    }

    private static int Slot(
        string key, string? assetPath, List<ModelMaterial> materials, Dictionary<string, int> slots)
    {
        if (slots.TryGetValue(key, out int existing)) return existing;

        int slot = materials.Count;
        slots[key] = slot;

        // Keep the cooked path. Don't rebuild it from the name at load.
        materials.Add(new ModelMaterial(NameWithoutExtension(key), null, Vector3.One, assetPath));
        return slot;
    }

    private static ModelMesh BuildMesh(
        in SmodelModel model, ModelGeometry geometry, in SmodelSubmesh submesh, int index, int materialSlot)
    {
        int start = (int)submesh.IndexStart;
        int count = (int)submesh.IndexCount;


        uint min = uint.MaxValue;
        uint max = 0;
        for (int i = 0; i < count; i++)
        {
            uint value = model.IndexAt(start + i);

            if (value < min) min = value;
            if (value > max) max = value;
        }

        int vertexCount = model.VertexCount;
        if (count == 0)
            return new ModelMesh("Submesh" + index, materialSlot, geometry, new((uint)start, 0),
                0, 0, new Aabb(submesh.BoundsMin, submesh.BoundsMax), true, true);

        if (max >= (uint)vertexCount)
        {
            // The reader does not validate index values; this is the only check.
            throw new SmodelFormatException(
                $"'{model.Source}' submesh {index} names vertex {max}, past the {vertexCount} in VBUF.");
        }

        return new ModelMesh("Submesh" + index, materialSlot, geometry, new((uint)start, (uint)count),
            (int)min, (int)(max - min) + 1, new Aabb(submesh.BoundsMin, submesh.BoundsMax), true, true);
    }

    private static int[] MeshIndices(int count)
    {
        var indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = i;
        return indices;
    }

    // Not Path.GetFileNameWithoutExtension: content paths use '/' on every host
    // and may be empty.
    private static string NameWithoutExtension(string path)
    {
        if (path.Length == 0) return string.Empty;

        int start = path.LastIndexOfAny(['/', '\\']) + 1;
        int dot = path.LastIndexOf('.');
        int end = dot > start ? dot : path.Length;
        return path[start..end];
    }
}
