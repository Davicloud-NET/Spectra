using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The catalog the engine gives a level: what it says about a cooked sound,
/// and how it says a sound is not there.
/// </summary>
public sealed class AssetSoundCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SpectraSoundCatalogTests", Guid.NewGuid().ToString("N"));

    private readonly AssetManager _assets;
    private readonly AssetSoundCatalog _catalog;

    public AssetSoundCatalogTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Sounds"));
        _assets = new AssetManager(NullLogger.Instance, _root, hotReloadEnabled: false);
        _catalog = new AssetSoundCatalog(_assets);
    }

    public void Dispose()
    {
        // First, or the open sounds still hold their files.
        _assets.Shutdown();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_cooked_sound_is_described_by_its_length_its_rate_and_its_loop()
    {
        Cooked("Sounds/hum.saudio", HandBuiltSaudio.Resident(frames: 480, sampleRate: 44_100, loopStart: 100, loopEnd: 400));

        _catalog.TryDescribe("Sounds/hum.wav", out SoundDescription sound, out string reason).ShouldBeTrue();

        reason.ShouldBeEmpty();
        sound.FrameCount.ShouldBe(480L);
        sound.SampleRate.ShouldBe(44_100);
        sound.Loop.ShouldBe(new LoopRegion(100, 400));
        sound.Markers.ShouldBeEmpty();
    }

    [Fact]
    public void A_cooked_sounds_markers_come_through_in_file_order()
    {
        byte[] markers = HandBuiltSaudio.MarkerBody((120, "now"), (300, "end"));
        Cooked("Sounds/line.saudio", HandBuiltSaudio.WithSections(480, (HandBuiltSaudio.MarkerTag, markers)));

        _catalog.TryDescribe("Sounds/line.wav", out SoundDescription sound, out _).ShouldBeTrue();

        sound.Loop.IsLooping.ShouldBeFalse();
        sound.Markers.ShouldBe([new AudioMarker(120, "now"), new AudioMarker(300, "end")]);
    }

    [Fact]
    public void Asking_about_a_sound_opens_it_so_it_is_loaded_before_it_plays()
    {
        Cooked("Sounds/hum.saudio", HandBuiltSaudio.Resident());

        _catalog.TryDescribe("Sounds/hum.wav", out _, out _).ShouldBeTrue();
        _catalog.TryDescribe("Sounds/hum.wav", out _, out _).ShouldBeTrue();

        _assets.AudioCount.ShouldBe(1);
    }

    [Fact]
    public void A_sound_that_is_not_in_the_content_is_not_there_and_says_so()
    {
        _catalog.TryDescribe("Sounds/nothing.wav", out SoundDescription sound, out string reason).ShouldBeFalse();

        reason.ShouldBe("no such file is in the project's content");
        sound.FrameCount.ShouldBe(0L);
    }

    [Fact]
    public void A_sound_that_was_never_cooked_is_not_there_and_says_to_cook_it()
    {
        File.WriteAllBytes(Path.Combine(_root, "Sounds", "raw.wav"), [1, 2, 3, 4]);

        _catalog.TryDescribe("Sounds/raw.wav", out _, out string reason).ShouldBeFalse();

        reason.ShouldContain("Sounds/raw.saudio");
        reason.ShouldContain("run scook over the project");
        reason.ShouldNotEndWith(".");
    }

    [Fact]
    public void A_cooked_file_the_engine_cannot_read_is_not_there_and_says_why()
    {
        Cooked("Sounds/broken.saudio", [9, 9, 9, 9, 9, 9, 9, 9]);

        _catalog.TryDescribe("Sounds/broken.wav", out _, out string reason).ShouldBeFalse();

        reason.ShouldContain("is not a .saudio this engine can read");
        reason.ShouldNotEndWith(".");
    }

    [Theory]
    [InlineData("../outside.wav")]
    [InlineData("C:/Windows/Media/ding.wav")]
    [InlineData("///")]
    public void A_path_that_leaves_the_content_is_not_there_and_does_not_throw(string path)
    {
        _catalog.TryDescribe(path, out _, out string reason).ShouldBeFalse();

        reason.ShouldBe("that is not a path inside the project's content");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_path_is_no_sound_at_all(string path)
    {
        _catalog.TryDescribe(path, out _, out string reason).ShouldBeFalse();

        reason.ShouldBe("no sound is set");
    }

    [Fact]
    public void A_catalog_whose_asset_manager_has_shut_down_still_does_not_throw()
    {
        Cooked("Sounds/hum.saudio", HandBuiltSaudio.Resident());
        _assets.Shutdown();

        _catalog.TryDescribe("Sounds/hum.wav", out _, out string reason).ShouldBeFalse();

        reason.ShouldNotBeEmpty();
    }

    private void Cooked(string contentPath, byte[] bytes) =>
        File.WriteAllBytes(Path.Combine(_root, contentPath.Replace('/', Path.DirectorySeparatorChar)), bytes);
}
