using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

// The rows of ASTB while a map is built: what the map names, by content path.
internal sealed class ScmapAssetTable
{
    private readonly List<ScmapAssetSource> _rows = [];

    // Without regard to case, as a pack finds an asset.
    private readonly Dictionary<string, uint> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _rows.Count;

    public IReadOnlyList<ScmapAssetSource> Rows => _rows;

    // The row of an asset, added when the path is new. Throws when the path
    // has a row of another kind.
    public uint Add(ScmapAssetSource asset)
    {
        string path = ContentRoot.NormalizeRelativePath(asset.ContentPath);

        if (_byPath.TryGetValue(path, out uint existing))
        {
            ScmapAssetSource had = _rows[(int)existing];
            if (had.Kind != asset.Kind)
            {
                throw new InvalidOperationException(
                    $"Asset '{path}' was added as {had.Kind} and again as {asset.Kind}. One path is one " +
                    "asset, so a reader would have to choose which kind it is, and choosing silently is how " +
                    "a material gets loaded as a model.");
            }

            return existing;
        }

        var index = (uint)_rows.Count;
        _rows.Add(asset with { ContentPath = path });
        _byPath[path] = index;
        return index;
    }

    // The only way a material gets a row. MaterialRef.Id is per-process and
    // must not be written.
    public uint AddMaterial(MaterialRef material, ulong contentHash)
    {
        if (material.IsDefault)
        {
            throw new InvalidOperationException(
                "The default material names no path, so it has no asset-table row. A surface that wears it " +
                "names no asset at all, which is what the engine's fallback to the default material already " +
                "means at load.");
        }

        if (!MaterialRegistry.TryGetPath(material, out string path))
        {
            throw new InvalidOperationException(
                $"Material id {material.Id} was never interned in this process, so there is no path to write. " +
                "An id is per-process interning order and means nothing in a file.");
        }

        return Add(new ScmapAssetSource(PackEntryKind.Material, path, contentHash));
    }

    // The section's body. pathStrings holds each row's path as a string index.
    public byte[] Build(uint[] pathStrings)
    {
        var body = new byte[ScmapFormat.AssetCountSize + (_rows.Count * ScmapFormat.AssetEntrySize)];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)_rows.Count);

        for (int i = 0; i < _rows.Count; i++)
        {
            var entry = new ScmapAssetEntry(_rows[i].Kind, pathStrings[i], _rows[i].ContentHash);
            MemoryMarshal.Write(span[(ScmapFormat.AssetCountSize + (i * ScmapFormat.AssetEntrySize))..], in entry);
        }

        return body;
    }
}
