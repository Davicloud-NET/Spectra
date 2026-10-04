using Spectra.Kitchen.Packs;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Packs;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The writer, checked against a hand-written parse of its own bytes
/// (<see cref="HandParsedPack"/>) rather than against a reader it shares types
/// with.
/// </summary>
public class PackWriterTests
{
    [Fact]
    public void A_written_pack_round_trips_its_header_fields()
    {
        var writer = new PackWriter(packSequence: 7);
        writer.Add("Textures/wall_brick.png", PackEntryKind.Image, Bytes(64));
        writer.Add("Models/crate.obj", PackEntryKind.Model, Bytes(31));

        byte[] pack = Write(writer);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);

        header.Magic.ShouldBe("SPAK");
        header.FormatVersion.ShouldBe(EngineInfo.PackFormatVersion);
        header.MinReaderVersion.ShouldBe(EngineInfo.MinimumReadablePackVersion);
        header.EntryCount.ShouldBe(2u);
        header.PackSequence.ShouldBe(7u);

        header.EntryTableOffset.ShouldBe((ulong)HandParsedPack.HeaderSize);

        header.NameTableOffset.ShouldBe(header.EntryTableOffset + (2 * (ulong)HandParsedPack.EntrySize));
        header.NameTableLength.ShouldBeGreaterThan(0ul);

        header.TotalFileSize.ShouldBe((ulong)pack.Length);

        header.EngineVersion.ShouldBe(
            ((uint)EngineInfo.MajorVersion << 20) | ((uint)EngineInfo.MinorVersion << 10) | EngineInfo.RevisionVersion);
    }

    [Fact]
    public void The_sorted_flag_is_set_because_v1_requires_it()
    {
        var writer = new PackWriter();
        writer.Add("Textures/a.png", PackEntryKind.Image, Bytes(4));

        HandParsedPack.Header header = HandParsedPack.ReadHeader(Write(writer));

        (header.Flags & (uint)PackFlags.EntriesSortedByAssetId).ShouldNotBe(0u);
        (header.Flags & (uint)PackFlags.NameTablePresent).ShouldNotBe(0u);
        (header.Flags & (uint)PackFlags.IsPatchPack).ShouldBe(0u);
        (header.Flags & (uint)PackFlags.IsModPack).ShouldBe(0u);
    }

    [Fact]
    public void A_written_pack_round_trips_its_entry_table()
    {
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["Textures/wall_brick.png"] = Bytes(64, seed: 1),
            ["Textures/floor.png"] = Bytes(1, seed: 2),
            ["Models/crate.obj"] = Bytes(300, seed: 3),
            ["Materials/brick.spectramat"] = Bytes(0, seed: 4),
        };

        var kinds = new Dictionary<string, PackEntryKind>(StringComparer.Ordinal)
        {
            ["Textures/wall_brick.png"] = PackEntryKind.Image,
            ["Textures/floor.png"] = PackEntryKind.Image,
            ["Models/crate.obj"] = PackEntryKind.Model,
            ["Materials/brick.spectramat"] = PackEntryKind.Material,
        };

        var writer = new PackWriter();
        foreach ((string path, byte[] payload) in payloads)
            writer.Add(path, kinds[path], payload);

        byte[] pack = Write(writer);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);
        List<HandParsedPack.Entry> entries = HandParsedPack.ReadEntries(pack, header);

        entries.Count.ShouldBe(payloads.Count);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (HandParsedPack.Entry entry in entries)
        {
            string name = HandParsedPack.ReadName(pack, header, entry);
            seen.Add(name).ShouldBeTrue($"'{name}' appeared twice");

            byte[] expected = payloads[name];
            entry.AssetId.ShouldBe(PackAssetId.From(name));
            entry.Kind.ShouldBe((byte)kinds[name]);
            entry.Codec.ShouldBe((byte)PackCodec.None);
            entry.StoredSize.ShouldBe((ulong)expected.Length);
            entry.UncompressedSize.ShouldBe((ulong)expected.Length);
            entry.NameLength.ShouldBe((ushort)Encoding.UTF8.GetByteCount(name));
            HandParsedPack.Payload(pack, entry).ToArray().ShouldBe(expected);
        }

        seen.ShouldBe(payloads.Keys, ignoreOrder: true);
    }

    [Fact]
    public void Entries_are_sorted_ascending_as_unsigned_one_hundred_and_twenty_eight_bit_values()
    {
        // 200 paths so some ids have the top bit set, where a signed compare
        // would order them wrongly.
        var writer = new PackWriter();
        for (int i = 0; i < 200; i++)
            writer.Add($"Textures/tile_{i:D3}.png", PackEntryKind.Image, Bytes(3, seed: i));

        byte[] pack = Write(writer);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);
        List<HandParsedPack.Entry> entries = HandParsedPack.ReadEntries(pack, header);

        for (int i = 1; i < entries.Count; i++)
        {
            entries[i].AssetId.ShouldBeGreaterThan(
                entries[i - 1].AssetId,
                $"entry {i} is not above entry {i - 1}");
        }

        // Guards the fixture: without a top-bit id the test proves nothing.
        entries.ShouldContain(e => e.AssetId >= (UInt128.One << 127), "no id had its top bit set");
    }

    [Fact]
    public void The_data_section_is_four_kilobyte_aligned_and_every_payload_is_sixteen_byte_aligned()
    {
        var writer = new PackWriter();
        for (int i = 0; i < 12; i++)
        {
            // Unaligned lengths, so a missing pad shows.
            writer.Add($"Models/prop_{i}.smodel", PackEntryKind.Model, Bytes(7 + (i * 13), seed: i));
        }

        byte[] pack = Write(writer);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);
        List<HandParsedPack.Entry> entries = HandParsedPack.ReadEntries(pack, header);

        (header.DataSectionOffset % 4096).ShouldBe(0ul);
        header.DataSectionOffset.ShouldBeGreaterThanOrEqualTo(
            header.NameTableOffset + header.NameTableLength);

        foreach (HandParsedPack.Entry entry in entries)
        {
            (entry.PayloadOffset % 16).ShouldBe(0ul, "a payload is reinterpreted in place and may not straddle 16 bytes");
            entry.PayloadOffset.ShouldBeGreaterThanOrEqualTo(header.DataSectionOffset);
        }
    }

    [Fact]
    public void Every_alignment_gap_is_explicitly_zero_filled()
    {
        var writer = new PackWriter();
        writer.Add("Textures/a.png", PackEntryKind.Image, Bytes(5, seed: 1));
        writer.Add("Textures/b.png", PackEntryKind.Image, Bytes(9, seed: 2));

        byte[] pack = Write(writer);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);
        List<HandParsedPack.Entry> entries = HandParsedPack.ReadEntries(pack, header);

        long tablesEnd = (long)(header.NameTableOffset + header.NameTableLength);
        for (long i = tablesEnd; i < (long)header.DataSectionOffset; i++)
            pack[i].ShouldBe((byte)0, $"byte {i} between the name table and the data section");

        // An unzeroed pad picks up buffer garbage and breaks byte identity.
        foreach (HandParsedPack.Entry entry in entries)
        {
            long end = (long)(entry.PayloadOffset + entry.StoredSize);
            long padded = (end + 15) & ~15L;
            for (long i = end; i < padded; i++)
                pack[i].ShouldBe((byte)0, $"pad byte {i} after a payload");
        }
    }

    [Fact]
    public void Two_writes_of_the_same_entries_are_byte_identical()
    {
        var writer = new PackWriter(packSequence: 3);
        writer.Add("Textures/wall_brick.png", PackEntryKind.Image, Bytes(64, seed: 1));
        writer.Add("Models/crate.obj", PackEntryKind.Model, Bytes(300, seed: 2), PackCodec.Deflate);
        writer.AddTombstone("Textures/gone.png");

        Write(writer).ShouldBe(Write(writer));
    }

    [Fact]
    public void Insertion_order_does_not_change_the_bytes()
    {
        string[] paths =
        [
            "Textures/wall_brick.png",
            "Models/crate.obj",
            "Materials/brick.spectramat",
            "Scripts/main.luau",
        ];

        var forward = new PackWriter();
        for (int i = 0; i < paths.Length; i++)
            forward.Add(paths[i], PackEntryKind.Raw, Bytes(16 + i, seed: i));

        var backward = new PackWriter();
        for (int i = paths.Length - 1; i >= 0; i--)
            backward.Add(paths[i], PackEntryKind.Raw, Bytes(16 + i, seed: i));

        Write(forward).ShouldBe(Write(backward));
    }

    [Fact]
    public void The_content_digest_covers_the_entry_table_to_the_end_excluding_itself()
    {
        var writer = new PackWriter();
        writer.Add("Textures/wall_brick.png", PackEntryKind.Image, Bytes(64, seed: 1));
        writer.Add("Models/crate.obj", PackEntryKind.Model, Bytes(33, seed: 2));

        byte[] pack = Write(writer);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);

        UInt128 expected = XxHash128.HashToUInt128(HandParsedPack.DigestedRegion(pack, header));
        HandParsedPack.StoredDigest(pack).ShouldBe(expected);
        HandParsedPack.StoredDigest(pack).ShouldBe(PackDigest.Compute(HandParsedPack.DigestedRegion(pack, header)));
    }

    [Fact]
    public void Flipping_one_payload_byte_changes_the_digest()
    {
        var writer = new PackWriter();
        writer.Add("Textures/wall_brick.png", PackEntryKind.Image, Bytes(64, seed: 1));

        byte[] pack = Write(writer);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);
        List<HandParsedPack.Entry> entries = HandParsedPack.ReadEntries(pack, header);

        UInt128 before = XxHash128.HashToUInt128(HandParsedPack.DigestedRegion(pack, header));
        pack[(int)entries[0].PayloadOffset] ^= 0xFF;
        UInt128 after = XxHash128.HashToUInt128(HandParsedPack.DigestedRegion(pack, header));

        after.ShouldNotBe(before);
        HandParsedPack.StoredDigest(pack).ShouldBe(before, "the stored digest is stale, which is what detects the edit");
    }

    [Fact]
    public void A_deflate_entry_records_both_sizes_and_inflates_back_to_the_original()
    {
        // Compressible, so the two sizes differ.
        byte[] payload = new byte[4096];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 7);

        var writer = new PackWriter();
        writer.Add("Maps/lobby.scmap", PackEntryKind.Map, payload, PackCodec.Deflate);

        byte[] pack = Write(writer);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);
        HandParsedPack.Entry entry = HandParsedPack.ReadEntries(pack, header)[0];

        entry.Codec.ShouldBe((byte)PackCodec.Deflate);
        entry.UncompressedSize.ShouldBe((ulong)payload.Length);
        entry.StoredSize.ShouldBeLessThan(entry.UncompressedSize);

        using var compressed = new MemoryStream(HandParsedPack.Payload(pack, entry).ToArray());
        using var inflate = new DeflateStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        inflate.CopyTo(inflated);

        inflated.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void A_tombstone_carries_no_payload_and_says_so_in_its_kind()
    {
        var writer = new PackWriter();
        writer.AddTombstone("Textures/removed_by_a_mod.png");

        byte[] pack = Write(writer);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);
        HandParsedPack.Entry entry = HandParsedPack.ReadEntries(pack, header)[0];

        entry.Kind.ShouldBe((byte)0xFF);
        entry.StoredSize.ShouldBe(0ul);
        entry.UncompressedSize.ShouldBe(0ul);
        HandParsedPack.ReadName(pack, header, entry).ShouldBe("Textures/removed_by_a_mod.png");
    }

    [Fact]
    public void An_empty_pack_is_still_a_valid_file()
    {
        byte[] pack = Write(new PackWriter());
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);

        header.Magic.ShouldBe("SPAK");
        header.EntryCount.ShouldBe(0u);
        header.NameTableOffset.ShouldBe(0ul);
        header.NameTableLength.ShouldBe(0ul);
        (header.Flags & (uint)PackFlags.NameTablePresent).ShouldBe(0u, "there is no name table to point at");
        header.DataSectionOffset.ShouldBe(4096ul);
        header.TotalFileSize.ShouldBe((ulong)pack.Length);
    }

    [Fact]
    public void A_pack_written_without_a_name_table_says_so_and_marks_every_entry_nameless()
    {
        var writer = new PackWriter(includeNameTable: false);
        writer.Add("Textures/wall_brick.png", PackEntryKind.Image, Bytes(16));

        byte[] pack = Write(writer);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(pack);
        HandParsedPack.Entry entry = HandParsedPack.ReadEntries(pack, header)[0];

        (header.Flags & (uint)PackFlags.NameTablePresent).ShouldBe(0u);
        header.NameTableOffset.ShouldBe(0ul);
        entry.NameOffset.ShouldBe(0xFFFFFFFFu, "zero is the first record's legitimate offset, so it cannot mean absent");
        entry.NameLength.ShouldBe((ushort)0);
    }

    [Fact]
    public void Adding_the_same_asset_twice_is_refused_naming_it()
    {
        var writer = new PackWriter();
        writer.Add("Textures/wall_brick.png", PackEntryKind.Image, Bytes(4));
        writer.Add("Textures/wall_brick.png", PackEntryKind.Image, Bytes(8));

        var thrown = Should.Throw<InvalidOperationException>(() => Write(writer));

        thrown.Message.ShouldContain("Textures/wall_brick.png");
        thrown.Message.ShouldContain("twice");
    }

    [Fact]
    public void An_id_collision_is_refused_naming_both_paths()
    {
        // Pack identity folds case, like the engine's asset caches.
        var writer = new PackWriter();
        writer.Add("Textures/Wall_Brick.png", PackEntryKind.Image, Bytes(4));
        writer.Add("Textures/wall_brick.png", PackEntryKind.Image, Bytes(8));

        var thrown = Should.Throw<InvalidOperationException>(() => Write(writer));

        thrown.Message.ShouldContain("Textures/Wall_Brick.png");
        thrown.Message.ShouldContain("Textures/wall_brick.png");
        thrown.Message.ShouldContain("collision");
    }

    [Fact]
    public void Two_spellings_of_one_asset_resolve_to_one_id()
    {
        PackAssetId.From("Textures/Wall_Brick.png").ShouldBe(PackAssetId.From("textures/wall_brick.png"));

        PackAssetId.From("/Textures/./wall.png").ShouldBe(PackAssetId.From("Textures\\wall.png"));
    }

    [Fact]
    public void A_tombstone_kind_is_refused_by_the_payload_taking_overload()
    {
        var writer = new PackWriter();

        var thrown = Should.Throw<ArgumentException>(
            () => writer.Add("Textures/a.png", PackEntryKind.Tombstone, Bytes(4)));

        thrown.Message.ShouldContain(nameof(PackWriter.AddTombstone));
    }

    [Fact]
    public void The_reserved_zstandard_codec_is_refused_by_name()
    {
        var writer = new PackWriter();

        var thrown = Should.Throw<NotSupportedException>(
            () => writer.Add("Maps/lobby.scmap", PackEntryKind.Map, Bytes(64), PackCodec.Zstandard));

        thrown.Message.ShouldContain("Zstandard");
        thrown.Message.ShouldContain(".NET 11");
    }

    [Fact]
    public void A_pack_cannot_be_both_a_patch_and_a_mod()
    {
        Should.Throw<ArgumentException>(
            () => new PackWriter(bandFlags: PackFlags.IsPatchPack | PackFlags.IsModPack));
    }

    [Fact]
    public void The_flags_the_writer_derives_cannot_be_asked_for()
    {
        Should.Throw<ArgumentException>(
            () => new PackWriter(bandFlags: PackFlags.EntriesSortedByAssetId));
    }

    [Fact]
    public void A_patch_pack_carries_its_band_flag_and_its_sequence()
    {
        var writer = new PackWriter(packSequence: 42, bandFlags: PackFlags.IsPatchPack);
        writer.Add("Textures/a.png", PackEntryKind.Image, Bytes(4));

        HandParsedPack.Header header = HandParsedPack.ReadHeader(Write(writer));

        (header.Flags & (uint)PackFlags.IsPatchPack).ShouldNotBe(0u);
        (header.Flags & (uint)PackFlags.IsModPack).ShouldBe(0u);
        header.PackSequence.ShouldBe(42u);
    }

    [Fact]
    public void Writing_to_a_file_produces_the_same_bytes_as_writing_to_a_stream()
    {
        var writer = new PackWriter(packSequence: 2);
        writer.Add("Textures/wall_brick.png", PackEntryKind.Image, Bytes(64, seed: 1));
        writer.Add("Models/crate.obj", PackEntryKind.Model, Bytes(300, seed: 2), PackCodec.Deflate);

        string path = Path.Combine(Path.GetTempPath(), $"spectra_pack_{Guid.NewGuid():N}.spack");
        try
        {
            writer.WriteToFile(path);

            byte[] fromFile = File.ReadAllBytes(path);
            fromFile.ShouldBe(Write(writer));

            HandParsedPack.ReadHeader(fromFile).TotalFileSize.ShouldBe((ulong)fromFile.Length);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static byte[] Write(PackWriter writer)
    {
        using var stream = new MemoryStream();
        writer.Write(stream);
        return stream.ToArray();
    }

    private static byte[] Bytes(int length, int seed = 0)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)((i * 31) + seed + 1);
        return bytes;
    }
}
