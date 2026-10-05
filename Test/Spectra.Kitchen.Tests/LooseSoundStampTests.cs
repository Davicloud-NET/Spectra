using Spectra.Kitchen.Cooking;
using SpectraEngine.Core;
using System;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

// What is kept with a cooked loose sound, so the editor's cache and a build's
// folder of sounds can tell without cooking whether it still stands.
public sealed class LooseSoundStampTests : IDisposable
{
    private const string Sound = "Sounds/guard_hey.wav";
    private const string Labels = "Sounds/guard_hey.markers.txt";

    private readonly TempProject _project = new();

    public void Dispose() => _project.Dispose();

    private string Root => _project.Layout.AssetsPath;

    [Fact]
    public void A_stamp_reads_back_as_it_was_written()
    {
        // Stereo, resampled and labelled: two diagnostics and three files.
        _project.WriteAsset(Sound, TempProject.Wav(frames: 441, sampleRate: 44_100, channels: 2));
        _project.WriteAsset(Labels, "0.005\tnow\n");
        LooseSoundResult result = LooseSoundCook.Run(Root, Sound);
        byte[] cooked = result.Cooked.ShouldNotBeNull();

        LooseSoundStamp read = Read(Bytes(LooseSoundStamp.Of(result)));

        read.SoundFormatVersion.ShouldBe(EngineInfo.AudioFormatVersion);
        read.Key.ShouldBe(result.Key);
        read.Dependencies.ShouldBe(result.Dependencies);
        read.Diagnostics.ShouldBe(result.Diagnostics);
        read.Diagnostics.Count.ShouldBeGreaterThan(1);
        read.HasSound.ShouldBeTrue();
        read.IsStampOf(cooked).ShouldBeTrue();
        read.IsStampOf(cooked.AsSpan(1)).ShouldBeFalse();
    }

    [Fact]
    public void Reading_leaves_the_stream_where_the_stamp_ends()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 96));
        byte[] stamp = Bytes(LooseSoundStamp.Of(LooseSoundCook.Run(Root, Sound)));

        using var stream = new MemoryStream([.. stamp, 1, 2, 3]);
        LooseSoundStamp.Read(stream);

        stream.Position.ShouldBe(stamp.Length);
    }

    [Fact]
    public void A_stamp_holds_until_a_file_the_cook_touched_changes_appears_or_goes()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 96));
        LooseSoundStamp bare = LooseSoundStamp.Of(LooseSoundCook.Run(Root, Sound));
        bare.Holds(Root, Sound).ShouldBeTrue();

        _project.WriteAsset(Labels, "0.001\tnow\n");
        bare.Holds(Root, Sound).ShouldBeFalse();

        LooseSoundStamp labelled = LooseSoundStamp.Of(LooseSoundCook.Run(Root, Sound));
        labelled.Holds(Root, Sound).ShouldBeTrue();

        File.Delete(Path.Combine(Root, "Sounds", "guard_hey.markers.txt"));
        labelled.Holds(Root, Sound).ShouldBeFalse();
        bare.Holds(Root, Sound).ShouldBeTrue();

        _project.WriteAsset(Sound, TempProject.Wav(frames: 96, seed: 3));
        bare.Holds(Root, Sound).ShouldBeFalse();
    }

    [Fact]
    public void A_stamp_does_not_hold_under_another_project_rate()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 96));
        LooseSoundStamp stamp = LooseSoundStamp.Of(LooseSoundCook.Run(Root, Sound));

        stamp.Holds(Root, Sound, new CookSettings { AudioSampleRate = 44_100 }).ShouldBeFalse();
    }

    [Fact]
    public void A_stamp_of_another_sound_format_version_does_not_hold()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 96));
        LooseSoundStamp stamp = LooseSoundStamp.Of(LooseSoundCook.Run(Root, Sound));

        LooseSoundStamp older = Read(Bytes(stamp with { SoundFormatVersion = stamp.SoundFormatVersion - 1 }));

        older.Holds(Root, Sound).ShouldBeFalse();
    }

    [Fact]
    public void A_stamp_of_a_wave_does_not_hold_for_the_wav_of_the_same_name()
    {
        const string Wave = "Sounds/guard_hey.wave";
        _project.WriteAsset(Wave, TempProject.Wav(frames: 96));
        LooseSoundStamp stamp = LooseSoundStamp.Of(LooseSoundCook.Run(Root, Wave));

        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));

        stamp.Holds(Root, Wave).ShouldBeTrue();
        stamp.Holds(Root, Sound).ShouldBeFalse();
    }

    [Fact]
    public void A_refused_sound_has_a_stamp_that_says_so_and_keeps_the_reason()
    {
        _project.WriteAsset(Sound, TempProject.Bytes(64));

        LooseSoundStamp stamp = Read(Bytes(LooseSoundStamp.Of(LooseSoundCook.Run(Root, Sound))));

        stamp.HasSound.ShouldBeFalse();
        stamp.Holds(Root, Sound).ShouldBeTrue();
        stamp.Diagnostics.Single(d => d.IsError).Id.ToString().ShouldBe("SC4001");
    }

    [Fact]
    public void Bytes_that_are_not_a_whole_stamp_are_refused()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 64, channels: 2));
        byte[] stamp = Bytes(LooseSoundStamp.Of(LooseSoundCook.Run(Root, Sound)));

        for (int length = 0; length < stamp.Length; length++)
            Should.Throw<InvalidDataException>(() => Read(stamp[..length]), $"cut to {length} bytes");

        Should.Throw<InvalidDataException>(() => Read(TempProject.Bytes(300)));

        byte[] otherVersion = [.. stamp];
        otherVersion[4] ^= 0xFF;
        Should.Throw<InvalidDataException>(() => Read(otherVersion));
    }

    private static byte[] Bytes(LooseSoundStamp stamp)
    {
        using var stream = new MemoryStream();
        stamp.Write(stream);
        return stream.ToArray();
    }

    private static LooseSoundStamp Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return LooseSoundStamp.Read(stream);
    }
}
