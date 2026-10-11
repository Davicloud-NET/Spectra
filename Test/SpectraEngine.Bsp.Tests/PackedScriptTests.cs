using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Scripting;
using System;
using System.IO;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The header a pack puts in front of a script, read from bytes written by
/// hand here, and the script source that takes a file with it or without.
/// </summary>
public sealed class PackedScriptTests : IDisposable
{
    private const string Door = "Scripts/door.luau";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"spectra_script_{Guid.NewGuid():N}");

    public PackedScriptTests() => Directory.CreateDirectory(Path.Combine(_root, "Scripts"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_packed_script_gives_back_the_text_behind_its_header()
    {
        byte[] entry = Packed("print(1)\n");

        PackedScript.IsPacked(entry).ShouldBeTrue();
        Encoding.UTF8.GetString(PackedScript.SourceOf(entry, Door)).ShouldBe("print(1)\n");
    }

    [Fact]
    public void A_packed_script_may_hold_no_text_at_all()
    {
        PackedScript.SourceOf(Packed(string.Empty), Door).Length.ShouldBe(0);
    }

    [Theory]
    [InlineData("print(1)\n")]
    [InlineData("")]
    [InlineData("SLU")]
    public void A_scripts_own_text_is_not_taken_for_a_packed_script(string text)
    {
        PackedScript.IsPacked(Encoding.UTF8.GetBytes(text)).ShouldBeFalse();
    }

    [Fact]
    public void A_version_this_build_does_not_read_is_refused_and_says_to_cook_again()
    {
        byte[] entry = Packed("print(1)\n");
        entry[4] = 2;

        string message = Should.Throw<InvalidDataException>(() => PackedScript.SourceOf(entry, Door)).Message;

        message.ShouldContain(Door);
        message.ShouldContain("version 2");
        message.ShouldContain("Cook the project again");
    }

    [Fact]
    public void A_payload_that_is_not_text_is_refused()
    {
        byte[] entry = Packed("print(1)\n");
        entry[6] = 2;

        Should.Throw<InvalidDataException>(() => PackedScript.SourceOf(entry, Door))
            .Message.ShouldContain("payload kind 2");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void A_length_that_is_not_what_follows_the_header_is_refused(int off)
    {
        byte[] entry = Packed("print(1)\n");
        entry[8] = (byte)(entry[8] + off);

        Should.Throw<InvalidDataException>(() => PackedScript.SourceOf(entry, Door));
    }

    [Fact]
    public void A_header_cut_short_is_refused()
    {
        byte[] whole = Packed("print(1)\n");

        Should.Throw<InvalidDataException>(() => PackedScript.SourceOf(whole.AsSpan(0, 10), Door));
        Should.Throw<InvalidDataException>(() => PackedScript.SourceOf("print(1)"u8, Door));
    }

    [Fact]
    public void The_source_reads_a_loose_file_and_a_packed_one_as_the_same_text()
    {
        byte[] text = Encoding.UTF8.GetBytes("print(1)\r\n");
        File.WriteAllBytes(Path.Combine(_root, "Scripts", "loose.luau"), text);
        File.WriteAllBytes(Path.Combine(_root, "Scripts", "packed.luau"), Packed("print(1)\r\n"));

        var source = new ContentScriptSource(new LooseFileSource(NullLogger.Instance, _root));

        source.Read("Scripts/loose.luau").ShouldBe(text);
        source.Read("Scripts/packed.luau").ShouldBe(text);
        source.Read("Scripts/absent.luau").ShouldBeNull();
    }

    [Fact]
    public void A_packed_file_this_build_cannot_read_is_no_script_and_the_log_says_why()
    {
        byte[] entry = Packed("print(1)\n");
        entry[4] = 9;
        File.WriteAllBytes(Path.Combine(_root, "Scripts", "door.luau"), entry);

        var log = new CapturingLogger();
        var source = new ContentScriptSource(new LooseFileSource(NullLogger.Instance, _root), log);

        source.Read(Door).ShouldBeNull();
        log.MessagesAt(LogLevel.Error).ShouldHaveSingleItem().ShouldContain(Door);
    }

    [Fact]
    public void A_source_over_no_content_reads_nothing()
    {
        new ContentScriptSource(null).Read(Door).ShouldBeNull();
    }

    // The format doc's layout, not the cook's writer: the reader is checked
    // against what is written down.
    private static byte[] Packed(string text)
    {
        byte[] body = Encoding.UTF8.GetBytes(text);
        var entry = new byte[16 + body.Length];

        entry[0] = 0x1B;
        entry[1] = (byte)'S';
        entry[2] = (byte)'L';
        entry[3] = (byte)'U';
        entry[4] = 1;                       // version, two bytes
        entry[6] = 1;                       // the payload is text
        entry[8] = (byte)body.Length;       // length, four bytes
        entry[9] = (byte)(body.Length >> 8);
        body.CopyTo(entry, 16);
        return entry;
    }
}
