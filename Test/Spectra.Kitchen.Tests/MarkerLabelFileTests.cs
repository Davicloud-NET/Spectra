using Spectra.Kitchen.Audio;
using System.Collections.Generic;
using System.Text;

namespace Spectra.Kitchen.Tests;

// The label file read for editors that cannot write cue points, in the layout
// Audacity exports a label track in.
public class MarkerLabelFileTests
{
    private const int Rate = 48_000;

    [Fact]
    public void The_label_file_sits_beside_the_sound_under_its_name()
    {
        MarkerLabelFile.PathFor("Sounds/door_open.wav").ShouldBe("Sounds/door_open.markers.txt");
        MarkerLabelFile.PathFor("guard_hey.wave").ShouldBe("guard_hey.markers.txt");
    }

    [Fact]
    public void A_label_is_a_marker_at_its_start_time()
    {
        SourceMarker[] markers = Read("0.250000\t0.250000\topen\n1.500000\t1.500000\tnow\n", out List<int> unreadable);

        markers.ShouldBe([new SourceMarker(12_000, "open"), new SourceMarker(72_000, "now")]);
        unreadable.ShouldBeEmpty();
    }

    [Fact]
    public void A_region_label_marks_where_the_region_starts()
    {
        Read("3.400000\t6.100000\tspeech\n", out _).ShouldBe([new SourceMarker(163_200, "speech")]);
    }

    [Fact]
    public void A_time_lands_on_the_nearest_frame()
    {
        // 0.00001 s is 0.48 of a frame and 0.00002 s is 0.96 of one.
        Read("0.00001\t0\ta\n0.00002\t0\tb\n", out _)
            .ShouldBe([new SourceMarker(0, "a"), new SourceMarker(1, "b")]);
    }

    [Fact]
    public void A_label_with_no_text_is_a_marker_with_no_name()
    {
        Read("1.0\t1.0\n2.0\t2.0\t\n", out List<int> unreadable)
            .ShouldBe([new SourceMarker(48_000, string.Empty), new SourceMarker(96_000, string.Empty)]);

        unreadable.ShouldBeEmpty();
    }

    [Fact]
    public void A_line_written_by_hand_as_a_time_and_a_name_is_a_named_marker()
    {
        Read("0.5\tnow\n1.0\t door opens \n", out List<int> unreadable)
            .ShouldBe([new SourceMarker(24_000, "now"), new SourceMarker(48_000, "door opens")]);

        unreadable.ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_end_time_still_leaves_the_name_in_the_third_column()
    {
        Read("0.5\t\tnow\n", out List<int> unreadable).ShouldBe([new SourceMarker(24_000, "now")]);
        unreadable.ShouldBeEmpty();
    }

    [Fact]
    public void Words_where_the_end_time_goes_in_a_three_column_line_are_reported()
    {
        // Either of the two could be the name, so the cook does not pick one.
        Read("0.5\tnow\tthe guard shouts\n1.0\tnow\t\n", out List<int> unreadable).ShouldBeEmpty();

        unreadable.ShouldBe([1, 2]);
    }

    [Fact]
    public void Frequency_lines_and_blank_lines_are_passed_over()
    {
        // Audacity writes a second line starting with a backslash for a label
        // that has a frequency range.
        string text = "1.0\t1.0\ta\n\\\t200.0\t4000.0\n\n   \n2.0\t2.0\tb\n";

        Read(text, out List<int> unreadable).Length.ShouldBe(2);
        unreadable.ShouldBeEmpty();
    }

    [Fact]
    public void A_line_that_is_not_a_label_is_skipped_and_its_number_reported()
    {
        string text = "1.0\t1.0\tgood\nnot a time\t1.0\tbad\njust words\n-2.0\t1.0\tbefore the start\n3.0\t3.0\talso good\n";

        SourceMarker[] markers = Read(text, out List<int> unreadable);

        markers.ShouldBe([new SourceMarker(48_000, "good"), new SourceMarker(144_000, "also good")]);
        unreadable.ShouldBe([2, 3, 4]);
    }

    [Fact]
    public void Windows_line_endings_and_a_byte_order_mark_change_nothing()
    {
        byte[] file = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("0.5\t0.5\tTür\r\n1.0\t1.0\tb\r\n")];
        var unreadable = new List<int>();

        MarkerLabelFile.Read(file, Rate, unreadable)
            .ShouldBe([new SourceMarker(24_000, "Tür"), new SourceMarker(48_000, "b")]);

        unreadable.ShouldBeEmpty();
    }

    [Fact]
    public void A_time_no_sound_could_reach_stays_a_marker_far_past_the_end()
    {
        // The cook reports it as past the end. It must not overflow on the way.
        Read("1e30\t1e30\tnever\n", out List<int> unreadable)[0].Frame.ShouldBe(uint.MaxValue);
        unreadable.ShouldBeEmpty();
    }

    private static SourceMarker[] Read(string text, out List<int> unreadable)
    {
        unreadable = [];
        return MarkerLabelFile.Read(Encoding.UTF8.GetBytes(text), Rate, unreadable);
    }
}
