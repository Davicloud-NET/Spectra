using System.Collections.Generic;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// The only route from a MaterialRef to an ASTB row. MaterialRef.Id is
// per-process and must never reach the file.
internal sealed class ScmapBakeAssets(ScmapBuilder builder)
{
    private readonly Dictionary<int, uint> _materials = [];

    public uint Material(MaterialRef material)
    {
        // Sentinel, not row 0: row 0 is a real asset.
        if (material.IsDefault) return ScmapFormat.NoAssetIndex;

        if (_materials.TryGetValue(material.Id, out uint existing)) return existing;

        uint index = builder.AddMaterial(material);
        _materials[material.Id] = index;
        return index;
    }

    public uint Model(string path) =>
        builder.AddAsset(new ScmapAssetSource(PackEntryKind.Model, path));
}
