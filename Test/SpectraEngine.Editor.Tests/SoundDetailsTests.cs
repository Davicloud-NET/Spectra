using Spectra.Kitchen.Audio;
using Spectra.Kitchen.Tests;
using SpectraEngine.Editor.Shell;
using System;
using System.Buffers.Binary;
using System.IO;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// What the content browser says about a selected sound file, read from the
/// WAV's own header.
/// </summary>
public sealed class SoundDetailsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spectra-sound-details-" + Guid.NewGuid().ToString("N"));

    public SoundDetailsTests() => Directory.CreateDirectory(_root);

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

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void A_mono_wav_says_its_length_channels_and_rate()
    {
        // A hundredth of a second at 44.1 kHz, under a kilobyte.
        string path = Write("click.wav", TempProject.Wav(frames: 441, sampleRate: 44_100));

        SoundDetails.Describe(path).ShouldBe("0.01 s  mono  44100 Hz  no loop region or markers");
    }

    [Fact]
    public void A_sound_under_a_second_keeps_its_thousandths()
    {
        var header = new WaveHeader(SampleRate: 48_000, Channels: 1, FrameCount: 120, HasLoop: false, MarkerCount: 0);

        SoundDetails.Describe(in header).ShouldStartWith("0.003 s  ");
    }

    [Fact]
    public void A_length_is_given_in_seconds_to_two_places()
    {
        string path = Write("beep.wav", TempProject.Wav(frames: 120, sampleRate: 96));

        SoundDetails.Describe(path).ShouldStartWith("1.25 s  mono  96 Hz");
    }

    [Fact]
    public void A_stereo_wav_with_a_loop_says_both()
    {
        string path = Write(
            "hum.wav", TempProject.Wav(frames: 100, sampleRate: 200, channels: 2, loopStart: 10, loopEnd: 89));

        SoundDetails.Describe(path).ShouldBe("0.5 s  stereo  200 Hz  loop region");
    }

    [Fact]
    public void A_wav_with_cue_points_says_how_many_markers_it_has()
    {
        byte[] wav = CuedWav.Add(
            TempProject.Wav(frames: 100, sampleRate: 100, loopStart: 0, loopEnd: 99),
            new CuedWav.Cue(1, 10, "open"),
            new CuedWav.Cue(2, 50, "close"));

        SoundDetails.Describe(Write("line.wav", wav)).ShouldBe("1 s  mono  100 Hz  loop region, 2 markers");
    }

    [Fact]
    public void One_cue_point_is_one_marker()
    {
        byte[] wav = CuedWav.Add(TempProject.Wav(frames: 100, sampleRate: 100), new CuedWav.Cue(1, 10, "now"));

        SoundDetails.Describe(Write("line.wav", wav)).ShouldEndWith("  1 marker");
    }

    [Fact]
    public void Markers_in_a_text_file_beside_the_wav_are_named_as_such()
    {
        string path = Write("speech.wav", TempProject.Wav(frames: 100, sampleRate: 100));
        File.WriteAllText(MarkerLabelFile.PathFor(path), "0.5\tnow\n");

        SoundDetails.Describe(path).ShouldBe("1 s  mono  100 Hz  markers in a text file");
    }

    [Fact]
    public void A_long_sound_is_given_in_minutes_and_seconds()
    {
        var header = new WaveHeader(SampleRate: 48_000, Channels: 2, FrameCount: 48_000L * 185, HasLoop: false, MarkerCount: 0);

        SoundDetails.Describe(in header).ShouldBe("3 min 05 s  stereo  48000 Hz  no loop region or markers");
    }

    [Fact]
    public void A_loop_the_cook_would_not_carry_is_not_called_a_loop_region()
    {
        // Backward, and past the end of the samples.
        string backward = Write("a.wav", TempProject.Wav(frames: 100, sampleRate: 100, loopStart: 10, loopEnd: 89, loopType: 2));
        string outside = Write("b.wav", TempProject.Wav(frames: 100, sampleRate: 100, loopStart: 10, loopEnd: 400));

        SoundDetails.Describe(backward).ShouldEndWith("no loop region or markers");
        SoundDetails.Describe(outside).ShouldEndWith("no loop region or markers");
    }

    [Theory]
    [InlineData("This is a text file with a sound's name.")]
    [InlineData("RIFF")]
    [InlineData("")]
    public void A_file_that_is_not_a_wav_says_so_in_plain_words(string contents)
    {
        string path = Write("notes.wav", System.Text.Encoding.ASCII.GetBytes(contents));

        SoundDetails.Describe(path).ShouldBe(SoundDetails.Unreadable);
        SoundDetails.Unreadable.ShouldBe("This file cannot be read as a sound. The engine reads uncompressed WAV.");
    }

    [Fact]
    public void A_compressed_wav_says_it_cannot_be_read()
    {
        byte[] wav = TempProject.Wav(frames: 100);

        // The format tag: 2 is ADPCM.
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20), 2);

        SoundDetails.Describe(Write("packed.wav", wav)).ShouldBe(SoundDetails.Unreadable);
    }

    [Fact]
    public void A_file_that_is_missing_says_it_cannot_be_read()
    {
        SoundDetails.Describe(Path.Combine(_root, "gone.wav")).ShouldBe(SoundDetails.Unreadable);
    }

    [Fact]
    public void The_header_is_read_without_the_samples()
    {
        byte[] wav = TempProject.Wav(frames: 100, sampleRate: 100);

        // Claim a gigabyte of samples. A reader that decoded them would fail.
        const int DataSizeOffset = 40;
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(DataSizeOffset), 1u << 30);

        using var stream = new CountingStream(wav);
        WaveHeader.TryRead(stream, out WaveHeader header).ShouldBeTrue();

        // What is really there, not what the size claims.
        header.FrameCount.ShouldBe(100);
        header.SampleRate.ShouldBe(100);
        stream.BytesRead.ShouldBeLessThan(64);
    }

    // Counts the bytes handed out, to show the samples were skipped.
    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long BytesRead { get; private set; }

        // A derived memory stream sends every read through this one.
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
    }
}
