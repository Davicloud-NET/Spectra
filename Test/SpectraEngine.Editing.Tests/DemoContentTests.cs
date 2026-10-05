using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Executable;
using System;
using System.IO;

namespace SpectraEngine.Editing.Tests;

// Where the demo reads its content from: the content root, and below it the
// folder its build cooks its sounds into.
public sealed class DemoContentTests : IDisposable
{
    private const string Sound = "Sounds/door_open.wav";
    private const string Cooked = "Sounds/door_open.saudio";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spectra_demo_content_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Never made, or locked. Neither is a test failure.
        }
    }

    private string ContentFolder => Path.Combine(_root, ContentRoot.DirectoryName);

    private string CookedFolder => Path.Combine(_root, DemoContent.CookedSoundsDirectoryName);

    [Fact]
    public void A_sound_the_build_cooked_loads_for_the_wav_in_the_content_root()
    {
        Write(ContentFolder, Sound, [1, 2, 3]);
        Write(CookedFolder, Cooked, HandBuiltSaudio.Resident(frames: 96));

        using AssetManager assets = Assets();

        assets.AudioExists(Sound).ShouldBeTrue();
        assets.LoadAudio(Sound).FrameCount.ShouldBe(96);
    }

    [Fact]
    public void A_cooked_sound_in_the_content_root_wins_over_the_one_the_build_cooked()
    {
        Write(ContentFolder, Cooked, HandBuiltSaudio.Resident(frames: 32));
        Write(CookedFolder, Cooked, HandBuiltSaudio.Resident(frames: 96));

        using AssetManager assets = Assets();

        assets.LoadAudio(Sound).FrameCount.ShouldBe(32);
    }

    [Fact]
    public void A_build_that_cooked_nothing_leaves_a_wav_with_no_sound_behind_it()
    {
        Write(ContentFolder, Sound, [1, 2, 3]);

        using AssetManager assets = Assets();

        Should.Throw<InvalidDataException>(() => assets.LoadAudio(Sound)).Message.ShouldContain("scook");
    }

    private AssetManager Assets()
    {
        ContentSourceStack content = DemoContent.Mount(NullLogger.Instance, ContentFolder, CookedFolder);
        return new AssetManager(NullLogger.Instance, ContentFolder, content, hotReloadEnabled: false);
    }

    private static void Write(string folder, string contentPath, byte[] bytes)
    {
        string full = ContentRoot.ResolveAbsolute(folder, contentPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? folder);
        File.WriteAllBytes(full, bytes);
    }
}
