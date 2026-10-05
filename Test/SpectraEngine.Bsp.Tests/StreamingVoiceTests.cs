using SpectraEngine.Core.Audio;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The streaming buffer queue: what lands in a buffer, loop wraps, starvation
/// restarts and seeks.
/// </summary>
public sealed class StreamingVoiceTests
{
    private const int Rate = 48000;
    private const int BufferFrames = 100;
    private const int BufferCount = 4;

    [Fact]
    public void A_priming_fill_queues_every_buffer_and_starts_the_source()
    {
        var backend = new FakeAudioBackend();
        StreamingVoice voice = Start(backend, Ramp(10_000, LoopRegion.None));

        backend.QueueDepth(voice.Source).ShouldBe(BufferCount);
        backend.StateOf(voice.Source).ShouldBe(AudioSourceState.Playing);
        voice.PositionFrames.ShouldBe(BufferFrames * BufferCount);

        // Ramp samples equal their frame index: buffer k starts at frame k*100.
        for (int k = 0; k < BufferCount; k++)
        {
            backend.Uploads[k].Length.ShouldBe(BufferFrames);
            backend.Uploads[k][0].ShouldBe((short)(k * BufferFrames));
        }
    }

    [Fact]
    public void A_loop_wraps_inside_a_single_uploaded_buffer()
    {
        // 250-frame region inside a 1000-frame sound, 100-frame buffers: one
        // buffer has to hold the end of the region followed by its start.
        var backend = new FakeAudioBackend();
        StreamingVoice voice = Start(backend, Ramp(1000, new LoopRegion(200, 450)));

        // Priming filled [0..100) [100..200) [200..300) [300..400).
        backend.Uploads.Count.ShouldBe(BufferCount);
        backend.Uploads[3][0].ShouldBe((short)300);

        // Next buffer: 50 frames up to 450, then 50 from 200.
        backend.Consume(voice.Source, 1);
        voice.Update().ShouldBeTrue();

        short[] straddling = backend.Uploads[^1];
        straddling.Length.ShouldBe(BufferFrames);
        for (int i = 0; i < 50; i++) straddling[i].ShouldBe((short)(400 + i));
        for (int i = 50; i < 100; i++) straddling[i].ShouldBe((short)(200 + (i - 50)));
    }

    [Fact]
    public void A_loop_shorter_than_one_buffer_repeats_inside_it()
    {
        var backend = new FakeAudioBackend();
        Start(backend, Ramp(200, new LoopRegion(100, 130)));

        backend.Uploads[0][99].ShouldBe((short)99);

        // Second buffer: 100..130 three times, then 10 frames of a fourth pass.
        short[] second = backend.Uploads[1];
        second.Length.ShouldBe(BufferFrames);
        for (int i = 0; i < 30; i++) second[i].ShouldBe((short)(100 + i));
        for (int i = 30; i < 60; i++) second[i].ShouldBe((short)(100 + (i - 30)));
        for (int i = 60; i < 90; i++) second[i].ShouldBe((short)(100 + (i - 60)));
        for (int i = 90; i < 100; i++) second[i].ShouldBe((short)(100 + (i - 90)));
    }

    [Fact]
    public void A_starved_queue_is_restarted_rather_than_mistaken_for_a_finished_sound()
    {
        // A starved source reports Stopped just like a finished one. Queue depth
        // tells them apart.
        var backend = new FakeAudioBackend();
        StreamingVoice voice = Start(backend, Ramp(1_000_000, LoopRegion.None));

        backend.Starve(voice.Source);
        backend.StateOf(voice.Source).ShouldBe(AudioSourceState.Stopped);

        voice.Update().ShouldBeTrue();

        voice.IsFinished.ShouldBeFalse();
        voice.UnderrunCount.ShouldBe(1);
        backend.StateOf(voice.Source).ShouldBe(AudioSourceState.Playing);
        backend.QueueDepth(voice.Source).ShouldBe(BufferCount);
    }

    [Fact]
    public void A_non_looping_stream_finishes_only_once_the_queue_has_drained()
    {
        // 250 frames in 100-frame buffers: the last fill is short, not padded.
        // Padding would click.
        var backend = new FakeAudioBackend();
        StreamingVoice voice = Start(backend, Ramp(250, LoopRegion.None));

        backend.QueueDepth(voice.Source).ShouldBe(3);
        backend.Uploads[2].Length.ShouldBe(50);

        // One buffer still queued, so not finished yet.
        backend.Consume(voice.Source, 2);
        voice.Update().ShouldBeTrue();
        voice.IsFinished.ShouldBeFalse();
        backend.QueueDepth(voice.Source).ShouldBe(1);

        backend.Consume(voice.Source, 1);
        voice.Update().ShouldBeFalse();
        voice.IsFinished.ShouldBeTrue();
    }

    [Fact]
    public void A_looping_stream_never_finishes_on_its_own()
    {
        var backend = new FakeAudioBackend();
        StreamingVoice voice = Start(backend, Ramp(300, new LoopRegion(0, 300)));

        for (int frame = 0; frame < 50; frame++)
        {
            backend.Consume(voice.Source, 2);
            voice.Update().ShouldBeTrue();
            backend.QueueDepth(voice.Source).ShouldBe(BufferCount);
        }

        voice.IsFinished.ShouldBeFalse();

        // Over thirty passes of the loop and never read past the region.
        voice.PositionFrames.ShouldBeLessThanOrEqualTo(300);
    }

    [Fact]
    public void A_seek_discards_what_was_already_queued_and_refills_from_the_new_position()
    {
        // AL only unqueues processed buffers, so the seek has to stop the source first.
        var backend = new FakeAudioBackend();
        StreamingVoice voice = Start(backend, Ramp(10_000, LoopRegion.None));

        int uploadsBefore = backend.Uploads.Count;
        voice.Seek(5000);

        backend.QueueDepth(voice.Source).ShouldBe(BufferCount);
        backend.StateOf(voice.Source).ShouldBe(AudioSourceState.Playing);
        voice.PositionFrames.ShouldBe(5000 + (BufferFrames * BufferCount));

        for (int k = 0; k < BufferCount; k++)
            backend.Uploads[uploadsBefore + k][0].ShouldBe((short)(5000 + (k * BufferFrames)));
    }

    [Fact]
    public void A_seek_into_the_middle_of_a_loop_keeps_looping_from_there()
    {
        var backend = new FakeAudioBackend();
        StreamingVoice voice = Start(backend, Ramp(1000, new LoopRegion(200, 450)));

        voice.Seek(400);

        // 50 frames to the loop end, then the wrap.
        short[] first = backend.Uploads[^BufferCount];
        for (int i = 0; i < 50; i++) first[i].ShouldBe((short)(400 + i));
        for (int i = 50; i < 100; i++) first[i].ShouldBe((short)(200 + (i - 50)));

        voice.IsFinished.ShouldBeFalse();
        backend.QueueDepth(voice.Source).ShouldBe(BufferCount);
    }

    [Fact]
    public void A_voice_started_part_way_fills_its_first_buffer_from_that_frame()
    {
        var backend = new FakeAudioBackend();
        StreamingVoice voice = Start(backend, Ramp(10_000, LoopRegion.None), startFrame: 5000);

        backend.StateOf(voice.Source).ShouldBe(AudioSourceState.Playing);
        backend.Uploads.Count.ShouldBe(BufferCount);
        for (int k = 0; k < BufferCount; k++)
            backend.Uploads[k][0].ShouldBe((short)(5000 + (k * BufferFrames)));
    }

    [Fact]
    public void A_loop_started_part_way_wraps_at_its_end_like_any_other()
    {
        var backend = new FakeAudioBackend();
        Start(backend, Ramp(1000, new LoopRegion(200, 450)), startFrame: 400);

        // 50 frames to the loop end, then the wrap.
        short[] first = backend.Uploads[0];
        for (int i = 0; i < 50; i++) first[i].ShouldBe((short)(400 + i));
        for (int i = 50; i < 100; i++) first[i].ShouldBe((short)(200 + (i - 50)));
    }

    [Fact]
    public void A_voice_started_at_the_end_of_its_sound_is_over_at_once()
    {
        var backend = new FakeAudioBackend();
        StreamingVoice voice = Start(backend, Ramp(250, LoopRegion.None), startFrame: 250);

        voice.IsFinished.ShouldBeTrue();
        backend.Uploads.ShouldBeEmpty();
    }

    [Fact]
    public void A_stereo_stream_interleaves_both_channels_into_the_buffer()
    {
        // A frame is one sample per channel: 100 stereo frames are 200 samples.
        var backend = new FakeAudioBackend();
        Start(backend, new RampSampleProvider(new AudioFormat(Rate, 2), 1000, LoopRegion.None));

        backend.Uploads[0].Length.ShouldBe(BufferFrames * 2);
        backend.Uploads[1][0].ShouldBe((short)BufferFrames);
    }

    [Fact]
    public void Detaching_destroys_every_buffer_the_voice_owned()
    {
        var backend = new FakeAudioBackend();
        StreamingVoice voice = Start(backend, Ramp(10_000, LoopRegion.None));
        backend.LiveBufferCount.ShouldBe(BufferCount);

        voice.Stop();
        voice.Detach();

        backend.LiveBufferCount.ShouldBe(0);
        voice.IsFinished.ShouldBeTrue();
    }

    private static RampSampleProvider Ramp(long frames, LoopRegion loop) =>
        new(new AudioFormat(Rate, 1), frames, loop);

    private static StreamingVoice Start(FakeAudioBackend backend, IAudioSampleProvider provider, long startFrame = 0)
    {
        backend.TryCreateSource(out uint source).ShouldBeTrue();
        return new StreamingVoice(
            backend, source, provider, AudioSourceSettings.Default, BufferCount, BufferFrames, startFrame);
    }
}
