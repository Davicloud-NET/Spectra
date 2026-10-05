using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>The captions the engine shows, written into the Output panel as they appear.</summary>
public sealed class CaptionOutputTests
{
    private readonly OutputLog _log = new();

    private static Caption Sound(long id, string text) =>
        new(id, CaptionKind.Sound, text, null, null, 1f, 0, 1);

    private static Caption Voice(long id, string text, string? speaker = null) =>
        new(id, CaptionKind.Voice, text, speaker, null, 1f, 0, 1);

    private static FrameSnapshot Showing(params Caption[] captions) => new() { Captions = captions };

    private string[] Lines() => [.. _log.Entries.Select(entry => entry.Text)];

    [Fact]
    public void A_caption_that_appears_is_written_once_however_long_it_shows()
    {
        var output = new CaptionOutput(_log);
        Caption[] shown = [Sound(1, "Door slides open")];

        output.Apply(Showing(shown));
        output.Apply(Showing(shown));
        output.Apply(Showing([shown[0] with { Audibility = 0.4f }]));

        Lines().ShouldBe(["Caption: [Door slides open]"]);
        _log.Entries[0].Severity.ShouldBe(OutputSeverity.Info);
    }

    [Fact]
    public void A_voice_line_names_its_speaker_and_a_sound_caption_is_in_brackets()
    {
        var output = new CaptionOutput(_log);

        output.Apply(Showing(Voice(1, "Going up.", "Lift"), Voice(2, "Hello?"), Sound(3, "Lift hums")));

        Lines().ShouldBe(["Caption: Lift: Going up.", "Caption: Hello?", "Caption: [Lift hums]"]);
    }

    [Fact]
    public void A_caption_that_joins_ones_already_written_is_the_only_new_line()
    {
        var output = new CaptionOutput(_log);
        output.Apply(Showing(Sound(1, "Button clicks")));

        output.Apply(Showing(Sound(1, "Button clicks"), Voice(2, "Going up.", "Lift")));

        Lines().ShouldBe(["Caption: [Button clicks]", "Caption: Lift: Going up."]);
    }

    [Fact]
    public void A_sound_that_shows_again_later_is_written_again()
    {
        var output = new CaptionOutput(_log);
        output.Apply(Showing(Sound(1, "Door slides open")));
        output.Apply(Showing());

        output.Apply(Showing(Sound(2, "Door slides shut")));

        Lines().ShouldBe(["Caption: [Door slides open]", "Caption: [Door slides shut]"]);
    }

    [Fact]
    public void A_line_break_the_writer_chose_becomes_a_space()
    {
        CaptionOutput.Line(Voice(1, "Stop right\nwhere you are.", "Guard"))
            .ShouldBe("Caption: Guard: Stop right where you are.");
    }

    [Fact]
    public void After_a_session_ends_the_next_engines_first_caption_is_written()
    {
        var output = new CaptionOutput(_log);
        output.Apply(Showing(Sound(7, "Lift hums")));

        output.Reset();
        output.Apply(Showing(Sound(1, "Air hums quietly")));

        Lines().ShouldBe(["Caption: [Lift hums]", "Caption: [Air hums quietly]"]);
    }

    [Fact]
    public void A_snapshot_with_no_captions_writes_nothing()
    {
        var output = new CaptionOutput(_log);

        output.Apply(new FrameSnapshot());

        _log.Entries.ShouldBeEmpty();
    }
}
