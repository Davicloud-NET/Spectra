using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Tests;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// A sound file as a kind of content: what the browser calls it, where the
/// pickers list it, and what dropping it into the viewport would do.
/// </summary>
public sealed class SoundContentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spectra-sound-content-" + Guid.NewGuid().ToString("N"));

    public SoundContentTests()
    {
        Write("Sounds/door_open.wav", TempProject.Wav(frames: 100));
        Write("Sounds/door_close.wav", TempProject.Wav(frames: 100));
        Write("Sounds/ambience/wind.wav", TempProject.Wav(frames: 100, channels: 2));
        Write("Sounds/door_open.markers.txt", "0.5\tnow\n"u8.ToArray());
        Write("Materials/door.spectramat", "shader lit\n"u8.ToArray());
        Write("Textures/door.png", [1, 2, 3]);
        Write("Models/door.obj", "o door\n"u8.ToArray());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp folder is not a test failure.
        }
    }

    private void Write(string relative, byte[] bytes)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full).ShouldNotBeNull());
        File.WriteAllBytes(full, bytes);
    }

    private async Task<ContentBrowserModel> BrowserAsync()
    {
        var browser = new ContentBrowserModel(NullLogger.Instance);
        browser.SetRoot(_root);
        await browser.Index.Walking;

        // Re-list after the walk. In the app the index's Changed event does
        // this, but no dispatcher delivers it here.
        browser.NavigateTo(_root);
        return browser;
    }

    private static ContentDragPayload Sound() =>
        new(ContentKind.Sound, "Sounds/door_open.wav", "door_open.wav");

    [Theory]
    [InlineData("Sounds/door_open.wav")]
    [InlineData("Sounds/DOOR_OPEN.WAV")]
    [InlineData(@"C:\project\Assets\Sounds\hum.wave")]
    public void A_wav_is_a_sound(string path)
    {
        ContentClassifier.Classify(path).ShouldBe(ContentKind.Sound);
    }

    [Theory]
    [InlineData("Sounds/door_open.markers.txt")]
    [InlineData("Sounds/door_open.saudio")]
    [InlineData("Sounds/door_open.en.vtt")]
    [InlineData("Sounds/music.mp3")]
    public void What_lies_beside_a_sound_is_not_one(string path)
    {
        ContentClassifier.Classify(path).ShouldBe(ContentKind.Other);
    }

    [Fact]
    public void The_word_for_a_sound_is_sound()
    {
        ContentClassifier.Label(ContentKind.Sound).ShouldBe("Sound");
    }

    [Fact]
    public async Task A_sound_row_has_a_word_of_its_own_and_the_path_a_level_stores()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "door_open.wav";

        ContentEntry entry = browser.Entries.ShouldHaveSingleItem();

        entry.Kind.ShouldBe(ContentKind.Sound);
        entry.KindLabel.ShouldBe("sound");
        entry.IsSound.ShouldBeTrue();
        entry.ContentPath.ShouldBe("Sounds/door_open.wav");
    }

    [Fact]
    public async Task Only_a_sound_row_has_a_play_button()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "door";

        browser.Entries.Where(entry => entry.IsSound).Select(entry => entry.Name)
            .ShouldBe(["door_close.wav", "door_open.wav"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_sounds_chip_lists_sounds_and_nothing_else()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "door";

        browser.Filter = ContentFilter.Sounds;

        browser.IsFilterSounds.ShouldBeTrue();
        browser.IsFilterAll.ShouldBeFalse();
        browser.Entries.Select(entry => entry.Name)
            .ShouldBe(["door_close.wav", "door_open.wav"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_sounds_chip_keeps_the_folders_of_a_folder_view()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.NavigateTo(Path.Combine(_root, "Sounds"));

        browser.Filter = ContentFilter.Sounds;

        // The label file is gone, the subfolder is not.
        browser.Entries.Select(entry => entry.Name)
            .ShouldBe(["ambience", "door_close.wav", "door_open.wav"], ignoreOrder: true);
    }

    [Fact]
    public async Task Another_chip_hides_the_sounds()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "door";

        browser.Filter = ContentFilter.Models;

        browser.IsFilterSounds.ShouldBeFalse();
        browser.Entries.Select(entry => entry.Name).ShouldBe(["door.obj"]);
    }

    [Fact]
    public async Task A_listing_says_whether_a_sound_is_in_it()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.HasSounds.ShouldBeFalse("the root lists folders only");

        browser.NavigateTo(Path.Combine(_root, "Sounds"));
        browser.HasSounds.ShouldBeTrue();

        browser.Filter = ContentFilter.Models;
        browser.HasSounds.ShouldBeFalse();

        browser.Filter = ContentFilter.All;
        browser.Query = "door";
        browser.HasSounds.ShouldBeTrue();

        browser.SetRoot(null);
        browser.HasSounds.ShouldBeFalse();
    }

    [Fact]
    public async Task A_files_size_reads_the_same_under_a_comma_culture()
    {
        // 1644 bytes. The details strip sets it beside a length such as 0.017 s.
        Write("Sounds/long.wav", TempProject.Wav(frames: 800));
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            ContentBrowserModel browser = await BrowserAsync();
            browser.Query = "long.wav";

            browser.Entries.ShouldHaveSingleItem().SizeLabel.ShouldBe("1.6 KB");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public async Task Selecting_a_sound_fills_the_details_strip_from_its_header()
    {
        ContentBrowserModel browser = await BrowserAsync();
        browser.Query = "wind";
        var landed = new TaskCompletionSource();
        browser.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(ContentBrowserModel.SelectedExtra) && browser.SelectedExtra.Length > 0)
                landed.TrySetResult();
        };

        browser.Select(browser.Entries.ShouldHaveSingleItem());
        await landed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        browser.SelectedDetails.ShouldStartWith("sound  Sounds/ambience/wind.wav");
        browser.SelectedExtra.ShouldBe("0.002 s  stereo  48000 Hz  no loop region or markers");
    }

    [Fact]
    public void The_catalog_lists_sounds_for_a_sound_row_and_nothing_else()
    {
        var catalog = new AssetCatalog(NullLogger.Instance);
        catalog.Rebuild(_root);

        List<AssetCatalogEntry> found = catalog.Search("", AssetCatalog.KindFor(AssetKind.Sound), 50);

        found.Select(entry => entry.ContentPath).ShouldBe(
            ["Sounds/ambience/wind.wav", "Sounds/door_close.wav", "Sounds/door_open.wav"]);
        found.ShouldAllBe(entry => entry.Kind == ContentKind.Sound);
    }

    [Fact]
    public void A_sound_is_found_by_its_name_and_carries_the_path_a_level_stores()
    {
        var catalog = new AssetCatalog(NullLogger.Instance);
        catalog.Rebuild(_root);

        AssetCatalogEntry wind = catalog.Search("wind", ContentKind.Sound, 5).ShouldHaveSingleItem();

        wind.ContentPath.ShouldBe("Sounds/ambience/wind.wav");
        wind.Stem.ShouldBe("wind");
        wind.Folder.ShouldBe("Sounds/ambience");
    }

    [Fact]
    public void A_search_for_another_kind_finds_no_sound()
    {
        var catalog = new AssetCatalog(NullLogger.Instance);
        catalog.Rebuild(_root);

        foreach (ContentKind kind in new[] { ContentKind.Material, ContentKind.Texture, ContentKind.Model })
            catalog.Search("door", kind, 50).ShouldAllBe(entry => entry.Kind == kind);
    }

    [Fact]
    public void A_sound_can_be_dropped_into_the_viewport()
    {
        AssetDropPolicy.CanPlace(ContentKind.Sound).ShouldBeTrue();
        AssetDropPolicy.Refuse(Sound(), hasSession: true, viewportAcceptsDrops: true).ShouldBeNull();
    }

    [Fact]
    public void A_sound_is_refused_in_the_words_a_model_is_when_nothing_can_take_it()
    {
        var model = new ContentDragPayload(ContentKind.Model, "Models/door.obj", "door.obj");

        AssetDropPolicy.Refuse(Sound(), hasSession: false, viewportAcceptsDrops: true)
            .ShouldBe(AssetDropPolicy.Refuse(model, hasSession: false, viewportAcceptsDrops: true));
        AssetDropPolicy.Refuse(Sound(), hasSession: true, viewportAcceptsDrops: false)
            .ShouldBe(AssetDropPolicy.Refuse(model, hasSession: true, viewportAcceptsDrops: false));
    }

    [Fact]
    public void A_file_that_cannot_be_dropped_is_told_that_sounds_can()
    {
        var shader = new ContentDragPayload(ContentKind.Shader, "Shaders/lit.spectrashade", "lit.spectrashade");

        AssetDropPolicy.Refuse(shader, hasSession: true, viewportAcceptsDrops: true).ShouldBe(
            "lit.spectrashade cannot be dropped into the scene; only models, materials and sounds can.");
    }

    [Theory]
    [InlineData(MaterialDropScope.Face)]
    [InlineData(MaterialDropScope.Brush)]
    public void Over_a_face_or_over_nothing_the_chip_says_the_sound_will_be_placed(MaterialDropScope scope)
    {
        // The scope is a material's business. A sound lands on what is under
        // the pointer, or in front of the camera when nothing is.
        ViewportDropPrompt prompt = ViewportDropPrompt.For(Sound(), hasSession: true, viewportAcceptsDrops: true, scope);

        prompt.IsVisible.ShouldBeTrue();
        prompt.Accepts.ShouldBeTrue();
        prompt.Headline.ShouldBe("Drop to place");
        prompt.Subject.ShouldBe("Sounds/door_open.wav");
        prompt.Reason.ShouldBeEmpty();
        prompt.Hint.ShouldBe("as a sound that plays this file");
        prompt.IconKey.ShouldBe(ViewportDropPrompt.SoundIcon);
    }

    [Fact]
    public void Where_no_drop_gets_a_chip_a_sound_gets_none_either()
    {
        // No project, or a native viewport. The refusal then goes to the
        // status line, in the policy's words.
        ViewportDropPrompt.For(Sound(), hasSession: false, viewportAcceptsDrops: true)
            .ShouldBe(ViewportDropPrompt.None);
        ViewportDropPrompt.For(Sound(), hasSession: true, viewportAcceptsDrops: false)
            .ShouldBe(ViewportDropPrompt.None);
    }

    [Fact]
    public void The_shell_shows_the_sound_chip_with_its_hint()
    {
        var shell = new ShellModel
        {
            DropPrompt = ViewportDropPrompt.For(Sound(), hasSession: true, viewportAcceptsDrops: true),
        };

        shell.DropVisible.ShouldBeTrue();
        shell.DropAccepts.ShouldBeTrue();
        shell.DropHasHint.ShouldBeTrue();
        shell.DropSubject.ShouldBe("Sounds/door_open.wav");
    }
}
