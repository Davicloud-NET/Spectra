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
/// <remarks>
/// One owned copy of VBUF and IBUF leaves the mapped input free to close. All
/// submeshes refer to ranges in that backing; compatibility arrays are lazy.
/// </remarks>
public static class CookedModelData
{
    /// <summary>
    /// Builds the CPU model. Pure, thread-safe, no GPU and no filesystem, so it
    /// runs on the thread pool exactly as an import does.
    /// </summary>
    /// <param name="model">The validated file.</param>
    /// <param name="relativePath">
    /// The content path the caller asked for - the AUTHORED one, so the model's
    /// <see cref="ModelData.SourcePath"/> reads the same whether it was cooked or
    /// imported.
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

        // Path -> slot, so two submeshes wearing one material share a slot and
        // AssetManager's own "which slots are referenced" pass keeps meaning what
        // it means. First appearance order, which is the file's order.
        var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < submeshCount; i++)
        {
            SmodelSubmesh submesh = model.Submeshes[i];
            meshes[i] = BuildMesh(model, geometry, submesh, i, MaterialSlot(model, submesh, materials, slots));
        }

        // A model with no material at all still gets one slot, for the reason the
        // importer gives: a submesh's material index must never dangle.
        if (materials.Count == 0) materials.Add(new ModelMaterial(string.Empty, null, Vector3.One));

        // ONE node holding every submesh, because a .smodel has no hierarchy to
        // rebuild: the cook baked every node transform into the vertices, which
        // is what one shared vertex buffer means. Said out loud here rather than
        // left for somebody to discover from an instantiated subtree that is one
        // node deep.
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

        // Refused rather than converted. See SmodelStandardLayout: the format
        // reserves a stride-copying fallback and this build has none, so the
        // honest answer is a message naming both layout ids rather than an
        // upload of floats in an order nothing agreed on.
        throw new SmodelFormatException(
            $"'{model.Source}' declares vertex layout 0x{model.VertexLayoutId:X8} at " +
            $"{model.VertexStrideFloats} floats per vertex, and this build uploads only the standard " +
            $"layout 0x{SmodelStandardLayout.LayoutId:X8} at {SmodelStandardLayout.StrideFloats}. Recook " +
            "the model.");
    }

    // The material slot this submesh draws with, creating one per distinct path.
    private static int MaterialSlot(
        in SmodelModel model,
        in SmodelSubmesh submesh,
        List<ModelMaterial> materials,
        Dictionary<string, int> slots)
    {
        if (!submesh.HasMaterial)
        {
            // The cook found no authored material for whatever the source file
            // named, and said so in its own log. Here it is one shared slot
            // resolving to the default material, which is what the loose path
            // does for a material with nothing usable on it.
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

        // The NAME is the stem and the PATH is what gets loaded. Both, because
        // the name is what a log line and an editor row read, and the path is the
        // only thing that must not be re-derived: a loader that rebuilt
        // "Materials/<name>.spectramat" from the stem would be a second spelling
        // of a rule the cooker already applied, and it would silently stop
        // working the day a material lives anywhere else.
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
            // The one index check the reader deliberately does not make, made
            // here because this is where the arithmetic would go out of range.
            // O(indices) is already paid by the copy above, so it costs nothing
            // that the load was not spending anyway.
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

    // Path-shaped or not, and never through Path.GetFileNameWithoutExtension:
    // a material path is content, so it may be the empty string, and the engine's
    // own separator is '/' whatever the host filesystem prefers.
    private static string NameWithoutExtension(string path)
    {
        if (path.Length == 0) return string.Empty;

        int start = path.LastIndexOfAny(['/', '\\']) + 1;
        int dot = path.LastIndexOf('.');
        int end = dot > start ? dot : path.Length;
        return path[start..end];
    }
}
