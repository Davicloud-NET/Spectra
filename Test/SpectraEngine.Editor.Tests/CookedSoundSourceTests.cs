using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Tests;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio;
using SpectraEngine.Editor.Sounds;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SpectraEngine.Editor.Tests;

// A project's loose WAV played in the editor: cooked on first use by the
// cook's own rule, kept in a cache, and cooked again when its files change.
public sealed class CookedSoundSourceTests : IDisposable
{
    private const string Sound = "Sounds/guard_hey.wav";
    private const string Cooked = "Sounds/guard_hey.saudio";
    private const string Labels = "Sounds/guard_hey.markers.txt";

    private readonly TempProject _project = new();
    private readonly CapturingLogger _log = new();
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
    public void A_loose_wav_loads_as_a_sound_with_the_source_mounted()
    {
        _project.WriteAsset(Sound, CuedWav.Add(
            TempProject.Wav(frames: 44_100, sampleRate: 44_100), new CuedWav.Cue(1, 11_025, "open")));

        AssetManager assets = Assets(Source());

        assets.AudioExists(Sound).ShouldBeTrue();

        AudioAsset sound = assets.LoadAudio(Sound);
        sound.ResolvedPath.ShouldBe(Cooked);
        sound.Format.ShouldBe(new AudioFormat(48_000, 1));
        sound.FrameCount.ShouldBe(48_000);
        sound.Samples.Length.ShouldBe(48_000);
        sound.Markers.ShouldBe([new AudioMarker(12_000, "open")]);
    }

    [Fact]
    public void The_bytes_are_the_ones_a_project_cook_puts_in_the_pack()
    {
        _project.WriteAsset(Sound, CuedWav.Add(
            TempProject.Wav(frames: 4_410, sampleRate: 44_100, loopStart: 100, loopEnd: 2_000),
            new CuedWav.Cue(1, 1_000, "open"),
            new CuedWav.Cue(2, 3_000)));
        _project.WriteAsset(Labels, "0.01\tnot read, the wav has cue points\n");

        Open(Source()).ShouldBe(PackedBytes());
    }

    [Fact]
    public void A_second_load_reads_the_cache_and_does_not_cook()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 441, sampleRate: 44_100));

        CookedSoundSource first = Source();
        byte[] cooked = Open(first);
        Open(first).ShouldBe(cooked);
        first.CookCount.ShouldBe(1);

        // A new source over the same folder is the next start of the editor.
        CookedSoundSource second = Source();
        Open(second).ShouldBe(cooked);
        second.CookCount.ShouldBe(0);
    }

    [Fact]
    public void A_changed_wav_is_cooked_again()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));
        CookedSoundSource source = Source();
        byte[] before = Open(source);

        _project.WriteAsset(Sound, TempProject.Wav(frames: 480, seed: 7));
        byte[] after = Open(source);

        source.CookCount.ShouldBe(2);
        after.ShouldNotBe(before);
        after.ShouldBe(PackedBytes());
    }

    [Fact]
    public void A_label_file_added_or_edited_cooks_the_sound_again_and_its_markers_show_up()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 48_000));
        CookedSoundSource source = Source();
        AssetManager assets = Assets(source);

        assets.LoadAudio(Sound).Markers.ShouldBeEmpty();

        _project.WriteAsset(Labels, "0.25\topen\n");
        assets.UnloadAudio(Sound).ShouldBeTrue();
        assets.LoadAudio(Sound).Markers.ShouldBe([new AudioMarker(12_000, "open")]);

        _project.WriteAsset(Labels, "0.5\tnow\n");
        assets.UnloadAudio(Sound).ShouldBeTrue();
        assets.LoadAudio(Sound).Markers.ShouldBe([new AudioMarker(24_000, "now")]);

        source.CookCount.ShouldBe(3);
    }

    [Fact]
    public void A_label_file_that_is_deleted_cooks_the_sound_again_and_its_markers_go()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 48_000));
        _project.WriteAsset(Labels, "0.25\topen\n");
        CookedSoundSource source = Source();
        AssetManager assets = Assets(source);

        assets.LoadAudio(Sound).Markers.ShouldBe([new AudioMarker(12_000, "open")]);

        File.Delete(ContentRoot.ResolveAbsolute(Root, Labels));
        assets.UnloadAudio(Sound).ShouldBeTrue();

        assets.LoadAudio(Sound).Markers.ShouldBeEmpty();
        source.CookCount.ShouldBe(2);
    }

    [Fact]
    public void A_cached_sound_of_another_sound_format_version_is_cooked_again()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));
        byte[] cooked = Open(Source());

        string entry = Directory.EnumerateFiles(_cache, "*", SearchOption.AllDirectories).ShouldHaveSingleItem();
        LooseSoundStamp stamp;
        using (FileStream stream = File.OpenRead(entry)) stamp = LooseSoundStamp.Read(stream);

        using (FileStream stream = File.Create(entry))
        {
            (stamp with { SoundFormatVersion = stamp.SoundFormatVersion - 1 }).Write(stream);
            stream.Write(cooked);
        }

        CookedSoundSource next = Source();
        Open(next).ShouldBe(cooked);
        next.CookCount.ShouldBe(1);
    }

    [Fact]
    public void A_cook_that_could_not_read_the_wav_is_not_kept_in_the_cache()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 96));
        string wav = ContentRoot.ResolveAbsolute(Root, Sound);
        CookedSoundSource source = Source();

        using (new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.SkipUnless(IsLockedAgainstReaders(wav), "This system lets a second reader into a locked file.");

            Should.Throw<InvalidDataException>(() => source.TryOpen(Cooked, out _));
        }

        // Kept, the entry would hold for as long as the WAV stays as it is.
        Directory.Exists(_cache).ShouldBeFalse();

        Open(source).Length.ShouldBeGreaterThan(0);
        source.CookCount.ShouldBe(2);
    }

    [Fact]
    public void A_wav_the_cook_refuses_fails_the_load_with_the_cooks_message()
    {
        _project.WriteAsset(Sound, TempProject.Bytes(64));
        AssetManager assets = Assets(Source());

        // There, and broken: not the same as missing.
        assets.AudioExists(Sound).ShouldBeTrue();

        var refused = Should.Throw<InvalidDataException>(() => assets.LoadAudio(Sound));
        refused.Message.ShouldStartWith("SC4001: ");
        refused.Message.ShouldContain($"'{Sound}' could not be decoded");

        new AssetSoundCatalog(assets).TryDescribe(Sound, out _, out string reason).ShouldBeFalse();
        reason.ShouldStartWith("SC4001: ");
    }

    [Fact]
    public void A_refusal_reaches_the_log_once_as_a_warning_that_names_the_file()
    {
        _project.WriteAsset(Sound, TempProject.Bytes(64));
        CookedSoundSource source = Source();
        AssetManager assets = Assets(source);

        Should.Throw<InvalidDataException>(() => assets.LoadAudio(Sound));
        Should.Throw<InvalidDataException>(() => assets.LoadAudio(Sound));

        source.CookCount.ShouldBe(1);

        string warning = _log.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain(Sound);
        warning.ShouldContain("SC4001");
        _log.MessagesAt(LogLevel.Error).ShouldBeEmpty();
    }

    [Fact]
    public void A_refused_wav_loads_once_it_is_repaired()
    {
        _project.WriteAsset(Sound, TempProject.Bytes(64));
        AssetManager assets = Assets(Source());
        Should.Throw<InvalidDataException>(() => assets.LoadAudio(Sound));

        _project.WriteAsset(Sound, TempProject.Wav(frames: 96));

        assets.LoadAudio(Sound).FrameCount.ShouldBe(96);
    }

    [Fact]
    public void A_warning_from_the_cook_is_said_again_at_the_next_start_without_cooking()
    {
        // Stereo and not named _2d: the cook warns that it will not be positional.
        _project.WriteAsset(Sound, TempProject.Wav(frames: 64, channels: 2));

        Open(Source());

        string warning = _log.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain(Sound);
        warning.ShouldContain("SC4003");

        CookedSoundSource next = Source();
        Open(next);
        Open(next);

        next.CookCount.ShouldBe(0);
        _log.MessagesAt(LogLevel.Warning).Count.ShouldBe(2);
    }

    [Fact]
    public void Two_warnings_about_one_sound_are_two_lines_each_with_its_code()
    {
        // Stereo, and a loop that plays back and forth, which is not carried.
        _project.WriteAsset(Sound, TempProject.Wav(frames: 64, channels: 2, loopStart: 8, loopEnd: 32, loopType: 1));

        Open(Source());

        IReadOnlyList<string> warnings = _log.MessagesAt(LogLevel.Warning);
        warnings.Count.ShouldBe(2);
        warnings.ShouldContain(w => w.StartsWith($"Sound {Sound}: SC4003: "));
        warnings.ShouldContain(w => w.StartsWith($"Sound {Sound}: SC4005: "));
    }

    [Fact]
    public void A_sound_that_comes_back_from_changed_files_says_so()
    {
        _project.WriteAsset(Sound, TempProject.Bytes(64));
        AssetManager assets = Assets(Source());
        Should.Throw<InvalidDataException>(() => assets.LoadAudio(Sound));
        _log.MessagesAt(LogLevel.Information).ShouldNotContain(m => m.Contains("cooked again"));

        _project.WriteAsset(Sound, TempProject.Wav(frames: 96));
        assets.LoadAudio(Sound);

        _log.MessagesAt(LogLevel.Information).ShouldContain($"Sound {Sound} changed and was cooked again");
    }

    [Fact]
    public void Callers_on_several_threads_asking_for_one_sound_cook_it_once()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 44_100, sampleRate: 44_100));
        CookedSoundSource source = Source();
        AssetManager assets = Assets(source);

        const int Callers = 8;
        var loaded = new AudioAsset?[Callers];
        var failures = new List<Exception>();
        using var start = new Barrier(Callers);

        var threads = new Thread[Callers];
        for (int i = 0; i < Callers; i++)
        {
            int slot = i;
            threads[i] = new Thread(() =>
            {
                try
                {
                    start.SignalAndWait(TestContext.Current.CancellationToken);
                    loaded[slot] = assets.LoadAudio(Sound);
                }
                catch (Exception ex)
                {
                    lock (failures) failures.Add(ex);
                }
            });
            threads[i].Start();
        }

        foreach (Thread thread in threads) thread.Join();

        failures.ShouldBeEmpty();
        source.CookCount.ShouldBe(1);
        loaded.ShouldAllBe(sound => ReferenceEquals(sound, loaded[0]));
        loaded[0].ShouldNotBeNull().FrameCount.ShouldBe(48_000);
    }

    [Fact]
    public void A_cooked_file_the_project_has_itself_wins_over_the_wav_beside_it()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));

        // Cooked from another sound, so the two cannot be mistaken.
        _project.WriteAsset("Sounds/other.wav", TempProject.Wav(frames: 96));
        _project.WriteAsset(Cooked, LooseSoundCook.Run(Root, "Sounds/other.wav").Cooked.ShouldNotBeNull());

        var assets = _project.Track(new AssetManager(
            NullLogger.Instance,
            Root,
            EditorContent.Mount(NullLoggerFactory.Instance, Root, _cache),
            hotReloadEnabled: false));

        assets.LoadAudio(Sound).FrameCount.ShouldBe(96);
        Directory.Exists(_cache).ShouldBeFalse();
    }

    [Fact]
    public void The_editors_own_stack_plays_a_loose_wav()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));

        var assets = _project.Track(new AssetManager(
            NullLogger.Instance,
            Root,
            EditorContent.Mount(NullLoggerFactory.Instance, Root, _cache),
            hotReloadEnabled: false));

        assets.LoadAudio(Sound).FrameCount.ShouldBe(480);
    }

    [Fact]
    public void A_wav_added_beside_a_wave_of_the_same_name_is_the_one_that_plays()
    {
        _project.WriteAsset("Sounds/guard_hey.wave", TempProject.Wav(frames: 96));
        CookedSoundSource source = Source();
        AssetManager assets = Assets(source);
        assets.LoadAudio(Sound).FrameCount.ShouldBe(96);

        // The cached entry was cooked from the .wave, which has not changed.
        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));
        assets.UnloadAudio(Sound).ShouldBeTrue();

        assets.LoadAudio(Sound).FrameCount.ShouldBe(480);
        source.CookCount.ShouldBe(2);
    }

    [Fact]
    public void Only_a_cooked_name_with_a_wav_behind_it_is_answered()
    {
        _project.WriteAsset(Sound, TempProject.Wav());
        _project.WriteAsset("Textures/wall.png", TempProject.Png());
        CookedSoundSource source = Source();

        source.Exists(Cooked).ShouldBeTrue();

        source.Exists(Sound).ShouldBeFalse();
        source.Exists("Sounds/absent.saudio").ShouldBeFalse();
        source.Exists("Textures/wall.png").ShouldBeFalse();
        source.Exists("Textures/wall.saudio").ShouldBeFalse();
        source.TryOpen("Sounds/absent.saudio", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_source_lists_one_cooked_name_per_sound_and_watches_the_wav()
    {
        _project.WriteAsset(Sound, TempProject.Wav());
        _project.WriteAsset("Sounds/Lift/hum.wave", TempProject.Wav());
        _project.WriteAsset(Labels, "0.5\tnow\n");
        CookedSoundSource source = Source();

        var listed = new List<string>();
        source.TryEnumerate("Sounds", ".saudio", listed);
        listed.Order(StringComparer.Ordinal).ShouldBe(["Sounds/Lift/hum.saudio", Cooked]);

        var textures = new List<string>();
        source.TryEnumerate(string.Empty, ".png", textures);
        textures.ShouldBeEmpty();

        source.TryGetWatchPath(Cooked, out string? watched).ShouldBeTrue();
        watched.ShouldBe(ContentRoot.ResolveAbsolute(Root, Sound));
    }

    private CookedSoundSource Source() => new(_log, Root, _cache);

    private AssetManager Assets(CookedSoundSource source)
    {
        var stack = new ContentSourceStack();
        stack.Mount(new LooseFileSource(NullLogger.Instance, Root));
        stack.Mount(source);

        return _project.Track(new AssetManager(NullLogger.Instance, Root, stack, hotReloadEnabled: false));
    }

    private static byte[] Open(CookedSoundSource source)
    {
        source.TryOpen(Cooked, out ContentBlob? blob).ShouldBeTrue();
        using ContentBlob opened = blob.ShouldNotBeNull();

        return opened.Span.ToArray();
    }

    private static bool IsLockedAgainstReaders(string path)
    {
        try
        {
            File.ReadAllBytes(path);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    // What scook writes for the project as it stands, read back out of the pack.
    private byte[] PackedBytes()
    {
        CookResult result = new CookSession(_project.Layout, new CookSettings { UseCache = false }).Run();
        result.Succeeded.ShouldBeTrue(string.Join('\n', result.Diagnostics));

        using var pack = new PackSource(NullLogger.Instance, result.OutputPath.ShouldNotBeNull());

        pack.TryOpen(Cooked, out ContentBlob? blob).ShouldBeTrue();
        using ContentBlob opened = blob.ShouldNotBeNull();

        return opened.Span.ToArray();
    }
}
