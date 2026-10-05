using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Tests;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Editor.Sounds;
using System;
using System.IO;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

// Where the editor keeps the sounds it cooked, and what it does with an entry
// it can no longer trust.
public sealed class SoundCookCacheTests : IDisposable
{
    private const string Sound = "Sounds/door_open.wav";
    private const string Cooked = "Sounds/door_open.saudio";

    private readonly TempProject _project = new();
    private readonly string _cache =
        Path.Combine(Path.GetTempPath(), "spectra_sound_cache_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _project.Dispose();

        try
        {
            Directory.Delete(_cache, recursive: true);
        }
        catch (IOException)
        {
            // Never made, or locked. Neither is a test failure.
        }
    }

    private string Root => _project.Layout.AssetsPath;

    [Fact]
    public void The_cache_for_a_project_is_not_inside_the_project()
    {
        string cache = SoundCookCache.DirectoryFor(Root);

        IsInside(cache, Root).ShouldBeFalse();
        IsInside(cache, _project.Root).ShouldBeFalse();

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        IsInside(cache, local.Length > 0 ? local : Path.GetTempPath()).ShouldBeTrue();
    }

    [Fact]
    public void One_content_root_has_one_cache_however_its_path_is_spelled()
    {
        string plain = SoundCookCache.DirectoryFor(Root);

        SoundCookCache.DirectoryFor(Root + Path.DirectorySeparatorChar).ShouldBe(plain);
        SoundCookCache.DirectoryFor(Path.Combine(Root, "..", "Assets")).ShouldBe(plain);

        using var other = new TempProject();
        SoundCookCache.DirectoryFor(other.Layout.AssetsPath).ShouldNotBe(plain);
    }

    [Fact]
    public void Cooking_a_sound_writes_nothing_into_the_content_root()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 441, sampleRate: 44_100));
        string[] before = FilesUnder(_project.Root);

        Open(Source());

        FilesUnder(_project.Root).ShouldBe(before);
        FilesUnder(_cache).ShouldHaveSingleItem();
    }

    [Fact]
    public void A_sound_keeps_one_cache_file_however_often_it_changes()
    {
        CookedSoundSource source = Source();

        for (int seed = 0; seed < 4; seed++)
        {
            _project.WriteAsset(Sound, TempProject.Wav(frames: 240, seed: seed));
            Open(source);
        }

        source.CookCount.ShouldBe(4);
        FilesUnder(_cache).ShouldHaveSingleItem();
    }

    [Fact]
    public void An_entry_cut_short_is_cooked_again()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));
        byte[] cooked = Open(Source());

        string entry = FilesUnder(_cache).ShouldHaveSingleItem();
        File.WriteAllBytes(entry, File.ReadAllBytes(entry)[..^100]);

        CookedSoundSource next = Source();
        Open(next).ShouldBe(cooked);
        next.CookCount.ShouldBe(1);
    }

    [Fact]
    public void An_entry_whose_sound_was_altered_on_disk_is_cooked_again()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));
        byte[] cooked = Open(Source());

        string entry = FilesUnder(_cache).ShouldHaveSingleItem();
        byte[] bytes = File.ReadAllBytes(entry);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(entry, bytes);

        CookedSoundSource next = Source();
        Open(next).ShouldBe(cooked);
        next.CookCount.ShouldBe(1);
    }

    [Fact]
    public void A_file_that_is_not_an_entry_is_cooked_over()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));
        byte[] cooked = Open(Source());

        string entry = FilesUnder(_cache).ShouldHaveSingleItem();
        File.WriteAllBytes(entry, TempProject.Bytes(300));

        Open(Source()).ShouldBe(cooked);

        // The repaired entry serves the next start.
        CookedSoundSource last = Source();
        Open(last).ShouldBe(cooked);
        last.CookCount.ShouldBe(0);
    }

    private CookedSoundSource Source() => new(NullLogger.Instance, Root, _cache);

    private static byte[] Open(CookedSoundSource source)
    {
        source.TryOpen(Cooked, out ContentBlob? blob).ShouldBeTrue();
        using ContentBlob opened = blob.ShouldNotBeNull();

        return opened.Span.ToArray();
    }

    private static string[] FilesUnder(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    private static bool IsInside(string path, string folder)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(folder), Path.GetFullPath(path));
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
