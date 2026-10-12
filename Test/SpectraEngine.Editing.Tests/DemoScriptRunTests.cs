using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Executable;
using System;
using System.IO;
using System.Text;

namespace SpectraEngine.Editing.Tests;

// The file the demo's --script switch is given: a script somebody wrote, or
// one out of a folder a cook wrote, which has the pack's header in front.
public sealed class DemoScriptRunTests : IDisposable
{
    private const string Text = "print(1)\n";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spectra_demo_script_" + Guid.NewGuid().ToString("N"));

    public DemoScriptRunTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Locked. Not a test failure.
        }
    }

    [Fact]
    public void A_script_somebody_wrote_is_read_as_it_is()
    {
        string path = Write("door.luau", Encoding.UTF8.GetBytes(Text));

        ScriptRun.ReadText(path, NullLogger.Instance).ShouldBe(Encoding.UTF8.GetBytes(Text));
    }

    [Fact]
    public void A_script_out_of_a_cooked_folder_is_read_without_the_packs_header()
    {
        string path = Write("door.luau", Packed(Text));

        ScriptRun.ReadText(path, NullLogger.Instance).ShouldBe(Encoding.UTF8.GetBytes(Text));
    }

    [Fact]
    public void A_packed_script_of_another_version_and_a_file_that_is_not_there_are_not_run()
    {
        byte[] newer = Packed(Text);
        newer[PackedScript.VersionOffset] = 2;

        ScriptRun.ReadText(Write("newer.luau", newer), NullLogger.Instance).ShouldBeNull();
        ScriptRun.ReadText(Path.Combine(_root, "absent.luau"), NullLogger.Instance).ShouldBeNull();
    }

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // By the header's own constants: the cook's writer is not referenced here.
    private static byte[] Packed(string text)
    {
        byte[] body = Encoding.UTF8.GetBytes(text);
        var entry = new byte[PackedScript.HeaderSize + body.Length];

        PackedScript.Magic.CopyTo(entry);
        entry[PackedScript.VersionOffset] = (byte)PackedScript.Version;
        entry[PackedScript.PayloadKindOffset] = (byte)PackedScriptPayload.Source;
        entry[PackedScript.PayloadLengthOffset] = (byte)body.Length;
        body.CopyTo(entry, PackedScript.HeaderSize);
        return entry;
    }
}
