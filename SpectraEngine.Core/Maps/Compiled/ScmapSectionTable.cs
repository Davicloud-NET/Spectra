using System;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

// Where each section of a .scmap this engine reads sits in the file, from the
// file's section table. A section it does not know is counted and skipped.
internal sealed class ScmapSectionTable
{
    public const int Strings = 0;
    public const int Asset = 1;
    public const int Meta = 2;
    public const int Node = 3;
    public const int Chunk = 4;
    public const int ChunkMesh = 5;
    public const int ChunkBsp = 6;
    public const int BrushSource = 7;
    public const int Entity = 8;
    public const int Connection = 9;
    public const int Collision = 10;
    public const int Light = 11;
    public const int HullMaterial = 12;
    public const int Script = 13;
    public const int NodeTag = 14;
    public const int NodeAttribute = 15;

    private const int KnownCount = 16;
    private const int Absent = -1;

    private readonly int[] _offset = new int[KnownCount];
    private readonly int[] _length = new int[KnownCount];

    private ScmapSectionTable() => _length.AsSpan().Fill(Absent);

    // Sections with a code this engine has no reader for.
    public int SkippedCount { get; private set; }

    public bool Has(int slot) => _length[slot] != Absent;

    // Zero for a section that is not there.
    public int OffsetOf(int slot) => Has(slot) ? _offset[slot] : 0;

    // Empty for a section that is not there.
    public ReadOnlySpan<byte> Body(ReadOnlySpan<byte> file, int slot) =>
        Has(slot) ? file.Slice(_offset[slot], _length[slot]) : default;

    // Reads the table behind a checked header. Every record is checked
    // against the file's length before anything is sliced by it.
    public static ScmapSectionTable Read(ReadOnlySpan<byte> file, in ScmapHeader header, string source)
    {
        long tableEnd = ScmapFormat.SectionTableOffset + ((long)header.SectionCount * ScmapFormat.SectionSize);
        if (tableEnd > file.Length)
        {
            throw new ScmapFormatException(
                $"'{source}' declares {header.SectionCount} sections, whose {ScmapFormat.SectionSize}-byte " +
                $"table would end at byte {tableEnd} of a {file.Length}-byte file.");
        }

        var table = new ScmapSectionTable();
        for (uint i = 0; i < header.SectionCount; i++)
        {
            ScmapSection record = MemoryMarshal.Read<ScmapSection>(
                file[(ScmapFormat.SectionTableOffset + ((int)i * ScmapFormat.SectionSize))..]);

            table.Place(source, in record, file.Length);
        }

        table.RequireWhatEveryMapCarries(source, in header);
        return table;
    }

    private void Place(string source, in ScmapSection record, int fileLength)
    {
        // Checked for unknown sections too, so the skip rule cannot let a
        // malformed file through.
        RequireSectionInFile(source, record, fileLength);

        if ((record.Offset % ScmapFormat.PayloadAlignment) != 0)
        {
            throw new ScmapFormatException(
                $"'{source}' section '{ScmapFormat.DescribeFourCc(record.Kind)}' starts at byte " +
                $"{record.Offset}, which is not a multiple of {ScmapFormat.PayloadAlignment}. Payloads " +
                "are reinterpreted in place, so an unaligned section start is a plane straddling a " +
                "boundary.");
        }

        if ((record.SectionFlags & ScmapSectionFlags.Compressed) != 0)
        {
            throw new ScmapFormatException(
                $"'{source}' section '{ScmapFormat.DescribeFourCc(record.Kind)}' is marked compressed. " +
                "That flag is reserved and no cook sets it: compression and a mapped zero-copy read are " +
                "mutually exclusive.");
        }

        int slot = KnownSlot(record.Kind);
        if (slot < 0)
        {
            SkippedCount++;
            return;
        }

        if (Has(slot))
        {
            throw new ScmapFormatException(
                $"'{source}' carries section '{ScmapFormat.DescribeFourCc(record.Kind)}' more than once. " +
                "A section names one region of the file, so a reader would have to choose, and choosing " +
                "silently is how half a map comes from one copy and half from the other.");
        }

        _offset[slot] = (int)record.Offset;
        _length[slot] = (int)record.Size;
    }

    private void RequireWhatEveryMapCarries(string source, in ScmapHeader header)
    {
        RequireSection(source, Strings, ScmapFormat.StringSection);
        RequireSection(source, Asset, ScmapFormat.AssetSection);
        RequireSection(source, Meta, ScmapFormat.MetaSection);
        RequireSection(source, Node, ScmapFormat.NodeSection);
        RequireSection(source, Chunk, ScmapFormat.ChunkDirectorySection);
        RequireSection(source, Entity, ScmapFormat.EntitySection);
        RequireSection(source, Connection, ScmapFormat.EntityConnectionSection);
        RequireSection(source, Collision, ScmapFormat.CollisionSection);
        RequireSection(source, Light, ScmapFormat.LightSection);
        RequireSection(source, Script, ScmapFormat.ScriptSection);

        // The header flag and the section table must agree about BRSH.
        bool flagged = (header.FileFlags & ScmapFlags.HasBrushSource) != 0;
        if (flagged != Has(BrushSource))
        {
            throw new ScmapFormatException(
                $"'{source}' has its HasBrushSource header flag {(flagged ? "set" : "clear")} and a BRSH section " +
                $"{(Has(BrushSource) ? "present" : "absent")}. The flag says the section is " +
                "there and nothing else, so the two can only disagree in a file nothing this engine wrote.");
        }
    }

    private static int KnownSlot(uint fourCc) => fourCc switch
    {
        ScmapFormat.StringSection => Strings,
        ScmapFormat.AssetSection => Asset,
        ScmapFormat.MetaSection => Meta,
        ScmapFormat.NodeSection => Node,
        ScmapFormat.ChunkDirectorySection => Chunk,
        ScmapFormat.ChunkMeshSection => ChunkMesh,
        ScmapFormat.ChunkBspSection => ChunkBsp,
        ScmapFormat.BrushSourceSection => BrushSource,
        ScmapFormat.EntitySection => Entity,
        ScmapFormat.EntityConnectionSection => Connection,
        ScmapFormat.CollisionSection => Collision,
        ScmapFormat.LightSection => Light,
        ScmapFormat.HullMaterialSection => HullMaterial,
        ScmapFormat.ScriptSection => Script,
        ScmapFormat.NodeTagSection => NodeTag,
        ScmapFormat.NodeAttributeSection => NodeAttribute,

        // Codes with no consumer (NBND, RGNI) and retired ones (BMDL, LUAB,
        // LUAS) are skipped like any unknown one.
        _ => -1,
    };

    private static void RequireSectionInFile(string source, in ScmapSection record, int fileLength)
    {
        // Subtract, don't add: offset + size can wrap in a corrupt file.
        if (record.Offset > (ulong)fileLength || record.Size > (ulong)fileLength - record.Offset)
        {
            throw new ScmapFormatException(
                $"'{source}' section '{ScmapFormat.DescribeFourCc(record.Kind)}' claims {record.Size} bytes " +
                $"at offset {record.Offset}, which runs past the {fileLength}-byte file.");
        }

        if (record.UncompressedSize != record.Size)
        {
            throw new ScmapFormatException(
                $"'{source}' section '{ScmapFormat.DescribeFourCc(record.Kind)}' declares {record.Size} " +
                $"stored bytes and {record.UncompressedSize} decoded ones while claiming no codec.");
        }
    }

    private void RequireSection(string source, int slot, uint fourCc)
    {
        if (Has(slot)) return;

        throw new ScmapFormatException(
            $"'{source}' has no '{ScmapFormat.DescribeFourCc(fourCc)}' section, which every .scmap must " +
            "carry.");
    }
}
