using Spectra.Kitchen.Audio;
using System.Buffers.Binary;
using System.Text;

namespace Spectra.Kitchen.Tests;

// What WaveDecoder reads out of a WAV's cue chunk and its labels.
public class WaveCueTests
{
    [Fact]
    public void Cue_points_come_back_as_frames_with_the_labels_that_name_them()
    {
        byte[] wav = CuedWav.Add(
            TempProject.Wav(frames: 1000),
            new CuedWav.Cue(1, 100, "open"),
            new CuedWav.Cue(2, 640, "now"));

        DecodedAudio decoded = WaveDecoder.Decode(wav);

        decoded.Markers.ShouldBe([new SourceMarker(100, "open"), new SourceMarker(640, "now")]);
    }

    [Fact]
    public void A_label_is_matched_by_cue_id_and_not_by_its_place_in_the_file()
    {
        // The labels sit in front of the cue points and in the other order.
        byte[] wav = TempProject.Wav(frames: 1000);
        wav = CuedWav.AppendChunk(wav, "LIST", CuedWav.LabelBody(
            new CuedWav.Cue(7, 0, "seven"), new CuedWav.Cue(3, 0, "three")));
        wav = CuedWav.AppendChunk(wav, "cue ", CuedWav.CueBody(
            new CuedWav.Cue(3, 300), new CuedWav.Cue(7, 700)));

        DecodedAudio decoded = WaveDecoder.Decode(wav);

        decoded.Markers.ShouldBe([new SourceMarker(300, "three"), new SourceMarker(700, "seven")]);
    }

    [Fact]
    public void A_cue_point_nothing_names_has_an_empty_label()
    {
        byte[] wav = CuedWav.Add(TempProject.Wav(frames: 1000), new CuedWav.Cue(1, 100));

        WaveDecoder.Decode(wav).Markers.ShouldBe([new SourceMarker(100, string.Empty)]);
    }

    [Fact]
    public void A_wav_with_no_cue_chunk_has_no_markers()
    {
        WaveDecoder.Decode(TempProject.Wav(frames: 1000)).Markers.ShouldBeEmpty();
    }

    [Fact]
    public void A_list_chunk_of_another_type_names_nothing()
    {
        // LIST/INFO holds the title and the artist. Its sub-chunks are not labels.
        byte[] info = CuedWav.LabelBody(new CuedWav.Cue(1, 0, "not a label"));
        Encoding.ASCII.GetBytes("INFO").CopyTo(info, 0);

        byte[] wav = CuedWav.AppendChunk(TempProject.Wav(frames: 1000), "cue ", CuedWav.CueBody(new CuedWav.Cue(1, 100)));
        wav = CuedWav.AppendChunk(wav, "LIST", info);

        WaveDecoder.Decode(wav).Markers.ShouldBe([new SourceMarker(100, string.Empty)]);
    }

    [Fact]
    public void A_cue_chunk_that_claims_more_points_than_it_holds_gives_the_ones_it_holds()
    {
        byte[] cue = CuedWav.CueBody(new CuedWav.Cue(1, 100), new CuedWav.Cue(2, 200));
        BinaryPrimitives.WriteUInt32LittleEndian(cue, 50);

        byte[] wav = CuedWav.AppendChunk(TempProject.Wav(frames: 1000), "cue ", cue);

        WaveDecoder.Decode(wav).Markers.Count.ShouldBe(2);
    }

    [Fact]
    public void A_label_reads_the_same_in_utf8_and_in_the_old_single_byte_encoding()
    {
        var door = new CuedWav.Cue(1, 100, "Tür auf");
        byte[] cue = CuedWav.CueBody(door);

        byte[] modern = CuedWav.AppendChunk(TempProject.Wav(frames: 1000), "cue ", cue);
        modern = CuedWav.AppendChunk(modern, "LIST", CuedWav.LabelBody(Encoding.UTF8, door));

        byte[] old = CuedWav.AppendChunk(TempProject.Wav(frames: 1000), "cue ", cue);
        old = CuedWav.AppendChunk(old, "LIST", CuedWav.LabelBody(Encoding.Latin1, door));

        WaveDecoder.Decode(modern).Markers[0].Label.ShouldBe("Tür auf");
        WaveDecoder.Decode(old).Markers[0].Label.ShouldBe("Tür auf");
    }

    [Fact]
    public void A_label_loses_the_spaces_around_it()
    {
        byte[] wav = CuedWav.Add(TempProject.Wav(frames: 1000), new CuedWav.Cue(1, 100, "  now \t"));

        WaveDecoder.Decode(wav).Markers[0].Label.ShouldBe("now");
    }
}
