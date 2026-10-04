using SpectraEngine.Core.Audio;

namespace SpectraEngine.Bsp.Tests;

// AL_LOOPING repeats a whole buffer, so loop regions are planned here and fed
// to a buffer queue. All positions are sample frames.
public sealed class AudioLoopCursorTests
{
    private const int Runs = 16;

    [Fact]
    public void A_sound_with_no_loop_reads_straight_through_and_then_is_exhausted()
    {
        var cursor = new AudioLoopCursor(1000, LoopRegion.None);
        Span<AudioSegment> runs = stackalloc AudioSegment[Runs];

        int count = cursor.Plan(runs, 400, out long planned);
        count.ShouldBe(1);
        runs[0].ShouldBe(new AudioSegment(0, 400));
        planned.ShouldBe(400);
        cursor.IsExhausted.ShouldBeFalse();

        // The tail fill is short, not padded: padding clicks.
        cursor.Plan(runs, 400, out _);
        count = cursor.Plan(runs, 400, out planned);
        count.ShouldBe(1);
        runs[0].ShouldBe(new AudioSegment(800, 200));
        planned.ShouldBe(200);
        cursor.IsExhausted.ShouldBeTrue();

        cursor.Plan(runs, 400, out planned).ShouldBe(0);
        planned.ShouldBe(0);
    }

    [Fact]
    public void An_intro_and_the_first_pass_through_the_loop_are_one_run()
    {
        var cursor = new AudioLoopCursor(1000, new LoopRegion(200, 600));
        Span<AudioSegment> runs = stackalloc AudioSegment[Runs];

        // Frames 0..600 are contiguous, so no run boundary at frame 200.
        int count = cursor.Plan(runs, 1000, out long planned);
        count.ShouldBe(2);
        runs[0].ShouldBe(new AudioSegment(0, 600));
        runs[1].ShouldBe(new AudioSegment(200, 400));
        planned.ShouldBe(1000);

        cursor.Position.ShouldBe(200);
        cursor.IsExhausted.ShouldBeFalse();
    }

    [Fact]
    public void A_loop_starting_at_zero_wraps_to_zero_and_never_exhausts()
    {
        var cursor = new AudioLoopCursor(500, new LoopRegion(0, 500));
        Span<AudioSegment> runs = stackalloc AudioSegment[Runs];

        int count = cursor.Plan(runs, 1200, out long planned);
        count.ShouldBe(3);
        runs[0].ShouldBe(new AudioSegment(0, 500));
        runs[1].ShouldBe(new AudioSegment(0, 500));
        runs[2].ShouldBe(new AudioSegment(0, 200));
        planned.ShouldBe(1200);

        cursor.IsExhausted.ShouldBeFalse();
        cursor.Position.ShouldBe(200);
    }

    [Fact]
    public void A_loop_region_shorter_than_one_buffer_repeats_inside_it()
    {
        // A 150-frame loop in a 1024-frame fill: six repetitions and part of a seventh.
        var cursor = new AudioLoopCursor(400, new LoopRegion(100, 250));
        Span<AudioSegment> runs = stackalloc AudioSegment[Runs];

        int count = cursor.Plan(runs, 1024, out long planned);

        runs[0].ShouldBe(new AudioSegment(0, 250));
        for (int i = 1; i < count; i++)
            runs[i].Offset.ShouldBe(100);

        long total = 0;
        for (int i = 0; i < count; i++) total += runs[i].Count;
        total.ShouldBe(planned);
        planned.ShouldBe(1024);

        for (int i = 1; i < count; i++)
            (runs[i].Offset + runs[i].Count).ShouldBeLessThanOrEqualTo(250);
    }

    [Fact]
    public void A_loop_shorter_than_the_run_budget_shortens_the_fill_instead_of_running_away()
    {
        // A 10-frame loop against 4096 frames would be 410 runs. Planning stops
        // when the span is full.
        var cursor = new AudioLoopCursor(100, new LoopRegion(0, 10));
        Span<AudioSegment> runs = stackalloc AudioSegment[4];

        int count = cursor.Plan(runs, 4096, out long planned);

        count.ShouldBe(4);
        planned.ShouldBe(40);
        cursor.IsExhausted.ShouldBeFalse();
    }

    [Fact]
    public void A_loop_length_that_is_not_a_multiple_of_the_buffer_crosses_the_wrap_mid_fill()
    {
        // 700-frame loop, 512-frame buffers: the wrap lands mid-fill at a
        // different offset each pass.
        const int BufferFrames = 512;
        var loop = new LoopRegion(300, 1000);
        var cursor = new AudioLoopCursor(1500, loop);
        Span<AudioSegment> runs = stackalloc AudioSegment[Runs];

        long expected = 0;
        for (int fill = 0; fill < 12; fill++)
        {
            int count = cursor.Plan(runs, BufferFrames, out long planned);
            planned.ShouldBe(BufferFrames, $"fill {fill} came up short");

            for (int i = 0; i < count; i++)
            {
                // Each run starts where the last one ended, in playback order.
                if (expected == loop.EndFrame) expected = loop.StartFrame;
                runs[i].Offset.ShouldBe(expected, $"fill {fill}, run {i} is discontinuous");

                expected = runs[i].Offset + runs[i].Count;
                expected.ShouldBeLessThanOrEqualTo(loop.EndFrame);
            }
        }

        cursor.IsExhausted.ShouldBeFalse();
    }

    [Fact]
    public void A_seek_into_the_middle_of_a_loop_keeps_looping_from_where_it_landed()
    {
        var cursor = new AudioLoopCursor(1000, new LoopRegion(200, 600));
        Span<AudioSegment> runs = stackalloc AudioSegment[Runs];

        cursor.Seek(450);
        cursor.Position.ShouldBe(450);

        int count = cursor.Plan(runs, 500, out long planned);
        count.ShouldBe(2);

        // Rest of this pass, then the loop start, not the intro.
        runs[0].ShouldBe(new AudioSegment(450, 150));
        runs[1].ShouldBe(new AudioSegment(200, 350));
        planned.ShouldBe(500);
        cursor.IsExhausted.ShouldBeFalse();
    }

    [Fact]
    public void A_seek_past_the_loop_plays_the_tail_and_finishes()
    {
        // Wrapping back into the loop here would make the outro unreachable.
        var cursor = new AudioLoopCursor(1000, new LoopRegion(200, 600));
        Span<AudioSegment> runs = stackalloc AudioSegment[Runs];

        cursor.Seek(800);
        int count = cursor.Plan(runs, 500, out long planned);

        count.ShouldBe(1);
        runs[0].ShouldBe(new AudioSegment(800, 200));
        planned.ShouldBe(200);
        cursor.IsExhausted.ShouldBeTrue();
    }

    [Fact]
    public void A_seek_before_the_loop_replays_the_intro()
    {
        var cursor = new AudioLoopCursor(1000, new LoopRegion(200, 600));
        Span<AudioSegment> runs = stackalloc AudioSegment[Runs];

        cursor.Plan(runs, 700, out _);
        cursor.Seek(50);

        int count = cursor.Plan(runs, 300, out long planned);
        count.ShouldBe(1);
        runs[0].ShouldBe(new AudioSegment(50, 300));
        planned.ShouldBe(300);
    }

    [Fact]
    public void A_seek_to_the_end_finishes_and_a_rewind_undoes_it()
    {
        var cursor = new AudioLoopCursor(1000, LoopRegion.None);
        Span<AudioSegment> runs = stackalloc AudioSegment[Runs];

        // Clamped, not refused: scrubbing to the end is a real gesture.
        cursor.Seek(5000);
        cursor.Position.ShouldBe(1000);
        cursor.IsExhausted.ShouldBeTrue();

        cursor.Rewind();
        cursor.IsExhausted.ShouldBeFalse();
        cursor.Plan(runs, 10, out long planned).ShouldBe(1);
        planned.ShouldBe(10);
    }

    [Fact]
    public void A_negative_seek_throws_rather_than_clamping_to_zero()
    {
        var cursor = new AudioLoopCursor(1000, LoopRegion.None);

        // Clamping would hide a sign error in the caller.
        Should.Throw<ArgumentOutOfRangeException>(() => cursor.Seek(-1));
    }

    [Fact]
    public void An_empty_loop_region_is_refused_because_it_is_a_hang()
    {
        // A zero-frame region would make the fill loop spin forever.
        Should.Throw<ArgumentOutOfRangeException>(() => new LoopRegion(400, 400));
        Should.Throw<ArgumentOutOfRangeException>(() => new LoopRegion(400, 100));
    }

    [Fact]
    public void A_loop_ending_past_the_sound_is_refused_at_the_cursor()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AudioLoopCursor(500, new LoopRegion(100, 900)));
    }

    [Fact]
    public void Frame_conversions_live_in_one_place()
    {
        var stereo = new AudioFormat(48000, 2);
        stereo.FramesToSamples(100).ShouldBe(200);
        stereo.SamplesToFrames(200).ShouldBe(100);
        stereo.FramesToSeconds(48000).ShouldBe(1.0);
        stereo.SecondsToFrames(0.5).ShouldBe(24000);

        var mono = new AudioFormat(48000, 1);
        mono.FramesToSamples(100).ShouldBe(100);

        // Same frames, same seconds, different sample counts.
        mono.FramesToSeconds(48000).ShouldBe(stereo.FramesToSeconds(48000));
        mono.FramesToSamples(48000).ShouldNotBe(stereo.FramesToSamples(48000));

        Should.Throw<ArgumentOutOfRangeException>(() => new AudioFormat(0, 1));
        Should.Throw<ArgumentOutOfRangeException>(() => new AudioFormat(48000, 6));
    }
}
