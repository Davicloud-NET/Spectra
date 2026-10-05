using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The demo's captions: every sound the start room plays has words, the cook
/// has nothing against them, and they come out of the feed while the level
/// is played.
/// </summary>
public sealed class DemoStartRoomCaptionTests : IClassFixture<CookedDemoSounds>, IClassFixture<CookedDemoCaptions>
{
    private const string Language = LanguageTag.Default;

    private const string LiftVoice = "Sounds/lift_voice.wav";

    private static readonly CharacterCommand East = new() { MoveForward = CharacterCommand.Axis(1f), Yaw = 0f };

    private readonly CookedDemoSounds _sounds;
    private readonly CookedDemoCaptions _captions;

    public DemoStartRoomCaptionTests(CookedDemoSounds sounds, CookedDemoCaptions captions)
    {
        _sounds = sounds;
        _captions = captions;
    }

    [Fact]
    public void The_caption_file_reads_with_no_problem()
    {
        CaptionFile file = DemoCaptionFile();

        file.Problems.ShouldBeEmpty();
        file.Entries.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_lifts_subtitles_read_with_no_problem_and_name_the_lift()
    {
        SubtitleFile file = LiftSubtitles();

        file.Unread.ShouldBeEmpty();
        file.Lines.ShouldHaveSingleItem().Speaker.ShouldBe("Lift");
    }

    [Fact]
    public void The_lifts_subtitle_line_starts_on_the_first_marker_and_ends_before_the_sound_does()
    {
        using var rig = new StartRoomSoundRig(_sounds);
        new AssetSoundCatalog(rig.Assets)
            .TryDescribe(LiftVoice, out SoundDescription sound, out string reason)
            .ShouldBeTrue(reason);

        CaptionLine line = LiftSubtitles().Lines[0];

        line.Start.ShouldBe(sound.SecondsAt(sound.Markers[0].Frame));
        line.End.ShouldBeLessThanOrEqualTo(sound.SecondsAt(sound.FrameCount));
    }

    [Fact]
    public void Every_sound_the_start_room_plays_has_a_caption_or_subtitles()
    {
        var log = new CapturingLogger();
        var library = new CaptionLibrary(new LooseFileSource(NullLogger.Instance, ContentRoot.Path), Language, log);
        List<string> sounds = SoundsTheStartRoomPlays();

        sounds.ShouldNotBeEmpty();
        foreach (string sound in sounds)
        {
            SoundCaptions found = library.Find(sound, Language).ShouldNotBeNull(sound);
            found.Kind.ShouldBe(sound == LiftVoice ? CaptionKind.Voice : CaptionKind.Sound, sound);
        }

        log.MessagesAt(LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void With_captions_set_to_all_the_doors_caption_shows_where_the_door_is_heard_opening()
    {
        using var rig = new StartRoomSoundRig(_sounds);
        rig.Captions.Mode = CaptionMode.All;
        rig.Session.Enter();
        string words = CaptionOn(rig, DemoPlayArea.DoorOpenSoundName);

        WalkUntilTheDoorSounds(rig);

        Caption shown = Showing(rig, words);
        shown.Kind.ShouldBe(CaptionKind.Sound);
        shown.Speaker.ShouldBeNull();
        shown.Position.ShouldBe(rig.Node(DemoPlayArea.DoorOpenSoundName).WorldPosition);
        shown.Audibility.ShouldBe(1f);
    }

    [Fact]
    public void The_doors_caption_stays_while_the_door_is_heard_and_goes_once_it_has_been_read()
    {
        using var rig = new StartRoomSoundRig(_sounds);
        rig.Captions.Mode = CaptionMode.All;
        rig.Session.Enter();
        string words = CaptionOn(rig, DemoPlayArea.DoorOpenSoundName);
        WalkUntilTheDoorSounds(rig);
        Caption first = Showing(rig, words);

        while (rig.IsPlaying(DemoPlayArea.DoorOpenSoundName))
        {
            Showing(rig, words).Audibility.ShouldBeGreaterThan(0f);
            rig.Tick(default);
        }

        // The words take longer to read than the door takes to open.
        (first.EarliestEnd - first.StartedAt).ShouldBe(CaptionTiming.ReadingSeconds(words), 1e-9);
        rig.Captions.Now.ShouldBeLessThan(first.EarliestEnd);

        while (rig.Captions.Now < first.EarliestEnd)
        {
            Showing(rig, words).Id.ShouldBe(first.Id);
            rig.Tick(default);
        }

        Texts(rig).ShouldNotContain(words);
    }

    [Fact]
    public void The_room_tones_caption_goes_once_it_has_been_read_though_the_tone_plays_on()
    {
        using var rig = new StartRoomSoundRig(_sounds);
        rig.Captions.Mode = CaptionMode.All;
        rig.Session.Enter();
        string words = CaptionOn(rig, DemoPlayArea.RoomToneName);

        rig.Tick(default);
        Caption shown = rig.Captions.Captions.ShouldHaveSingleItem();
        shown.Text.ShouldBe(words);

        // Ten seconds standing in the room it hums in.
        rig.Idle(600);

        rig.IsPlaying(DemoPlayArea.RoomToneName).ShouldBeTrue();
        rig.Captions.Captions.ShouldBeEmpty();

        // And it did not come back in that time.
        rig.Captions.LastId.ShouldBe(shown.Id);
    }

    [Fact]
    public void With_the_setting_the_engine_starts_with_only_the_lifts_line_shows_and_it_names_the_lift()
    {
        using var rig = new StartRoomSoundRig(_sounds);
        rig.Session.Enter();
        FuncMoveLinear lift = rig.Live<FuncMoveLinear>(DemoPlayArea.LiftName);
        var seen = new Dictionary<long, Caption>();

        // Long enough for the click, the line, the hum and the clunk.
        rig.PressTheLiftButton();
        for (int i = 0; i < 300 && lift.TicksTravelled < lift.TravelTicks; i++)
        {
            rig.Tick(default);
            foreach (Caption caption in rig.Captions.Captions)
                seen.TryAdd(caption.Id, caption);
        }

        rig.Captions.Mode.ShouldBe(CaptionMode.Voice);
        lift.TicksTravelled.ShouldBe(lift.TravelTicks);

        Caption shown = seen.Values.ShouldHaveSingleItem();
        shown.Kind.ShouldBe(CaptionKind.Voice);
        shown.Speaker.ShouldBe("Lift");
        shown.Text.ShouldBe(LiftSubtitles().Lines[0].Text);

        // The feed counts every caption it has shown.
        rig.Captions.LastId.ShouldBe(1);
    }

    [Fact]
    public void The_cook_says_nothing_about_captions_and_subtitles_but_how_many_sounds_have_a_caption()
    {
        CookResult cook = _captions.Cook;

        cook.Succeeded.ShouldBeTrue(Describe(cook));

        CookDiagnostic said = cook.Diagnostics.Where(IsAboutCaptions).ShouldHaveSingleItem(Describe(cook));
        said.Id.ShouldBe(CookDiagnosticCodes.CaptionLanguageSummary);
        said.Severity.ShouldBe(CookDiagnosticSeverity.Info);
        said.File.ShouldBe(CaptionFile.PathFor(Language));
        said.Message.ShouldContain($"{DemoCaptionFile().Entries.Count} sounds");
    }

    [Fact]
    public void The_cook_packs_the_caption_file_and_the_subtitle_file()
    {
        Dictionary<string, long> packed = _captions.Cook.Assets
            .SelectMany(asset => asset.Outputs)
            .ToDictionary(output => output.Path, output => output.Length);

        foreach (string path in new[] { CaptionFile.PathFor(Language), SubtitlePath.For(LiftVoice, Language) })
            packed.ShouldContainKeyAndValue(path, Bytes(path).LongLength);
    }

    // Walks at the door until its sound has started.
    private static void WalkUntilTheDoorSounds(StartRoomSoundRig rig)
    {
        for (int i = 0; i < 300 && !rig.IsPlaying(DemoPlayArea.DoorOpenSoundName); i++)
            rig.Tick(in East);

        rig.IsPlaying(DemoPlayArea.DoorOpenSoundName).ShouldBeTrue();
    }

    // The words the caption file has for the sound on a node.
    private static string CaptionOn(StartRoomSoundRig rig, string node)
    {
        rig.Node(node).Entity.ShouldNotBeNull().TryGetValue("sound", out string sound).ShouldBeTrue();
        DemoCaptionFile().TryGetText(sound, out string? words).ShouldBeTrue(sound);

        return words.ShouldNotBeNull();
    }

    private static Caption Showing(StartRoomSoundRig rig, string words) =>
        rig.Captions.Captions.Single(caption => caption.Text == words);

    private static string[] Texts(StartRoomSoundRig rig) =>
        [.. rig.Captions.Captions.Select(caption => caption.Text)];

    private static CaptionFile DemoCaptionFile() => CaptionFileReader.Read(Bytes(CaptionFile.PathFor(Language)));

    private static SubtitleFile LiftSubtitles()
    {
        string path = SubtitlePath.For(LiftVoice, Language);
        return SubtitleReader.Read(Bytes(path), path);
    }

    private static byte[] Bytes(string contentPath) =>
        File.ReadAllBytes(ContentRoot.ResolveAbsolute(ContentRoot.Path, contentPath));

    private static List<string> SoundsTheStartRoomPlays()
    {
        var scene = new Scene("StartRoom");
        DemoPlayArea.Build(scene, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default);

        var sounds = new List<string>();
        foreach (SceneNode node in scene.Root.Traverse())
        {
            if (node.Entity is { ClassName: "point_sound" } entity && entity.TryGetValue("sound", out string sound))
                sounds.Add(sound);
        }

        return sounds;
    }

    // The cook's codes for captions and subtitles are 4101 to 4199.
    private static bool IsAboutCaptions(CookDiagnostic said) =>
        said.Id.IsCookCode && said.Id.Number is >= 4101 and <= 4199;

    private static string Describe(CookResult cook) =>
        string.Join(Environment.NewLine, cook.Diagnostics.Select(static said => said.ToString()));
}
