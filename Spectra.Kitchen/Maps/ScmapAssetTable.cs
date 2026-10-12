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

    // The other spellings of a script's path, each with a row of its own.
    private readonly Dictionary<string, uint> _scriptSpellings = new(StringComparer.Ordinal);

    public int Count => _rows.Count;

    public IReadOnlyList<ScmapAssetSource> Rows => _rows;

    // The row of an asset, added when the path is new.
    public uint Add(ScmapAssetSource asset)
    {
        string path = ContentRoot.NormalizeRelativePath(asset.ContentPath);

        if (!_byPath.TryGetValue(path, out uint first))
        {
            _byPath[path] = (uint)_rows.Count;
            return Append(asset with { ContentPath = path });
        }

        ScmapAssetSource had = _rows[(int)first];
        if (had.Kind != asset.Kind)
            throw new ScmapAssetKindException(path, had.Kind, asset.Kind);

        // A script prints its path in front of a line, as its node spells it.
        // So a second spelling of one file is a second row.
        if (asset.Kind != PackEntryKind.Script || string.Equals(had.ContentPath, path, StringComparison.Ordinal))
            return first;

        if (_scriptSpellings.TryGetValue(path, out uint spelled))
            return spelled;

        _scriptSpellings[path] = (uint)_rows.Count;
        return Append(asset with { ContentPath = path });
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

    private uint Append(ScmapAssetSource row)
    {
        _rows.Add(row);
        return (uint)(_rows.Count - 1);
    }
}
