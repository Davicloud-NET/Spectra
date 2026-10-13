using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The order a script is handed a node's tags in: by the bytes of their
/// UTF-8. The order a node holds them in is the order it got them.
/// </summary>
public sealed class TagByteOrderTests
{
    [Fact]
    public void Tags_sort_by_their_bytes_so_digits_come_before_capitals_and_capitals_before_small_letters()
    {
        string[] tags = ["b", "a", "c", "B", "ab", "", "a b", "10", "9", "_", "Lava", "lava"];

        Array.Sort(tags, TagName.CompareBytes);

        tags.ShouldBe(["", "10", "9", "B", "Lava", "_", "a", "a b", "ab", "b", "c", "lava"]);
    }

    [Fact]
    public void A_tag_is_equal_to_itself_and_to_no_other()
    {
        TagName.CompareBytes("Lava", "Lava").ShouldBe(0);
        TagName.CompareBytes("Lava", "lava").ShouldBeLessThan(0);
        TagName.CompareBytes("lava", "Lava").ShouldBeGreaterThan(0);
        TagName.CompareBytes("Lava", "Lava ").ShouldBeLessThan(0);
    }

    // The samples are made from code points: an escape in source does not
    // survive every editing tool.
    [Fact]
    public void The_order_is_that_of_the_UTF8_bytes_also_where_UTF16_orders_the_other_way()
    {
        string[] samples =
        [
            "", "a", "ab", "b", "z", S(0x7F), S(0x80), S(0xFC), S(0x7FF), S(0x800), S(0xD7FF), S(0xE000),
            S(0xFF5E), S(0xFFFD), S(0x10000), S(0x1F600), S(0x10FFFF), "a" + S(0x1F600), "a" + S(0xFF5E),
            S(0x1F600) + "a", S(0x1F600) + S(0xFF5E), S(0x1F600) + S(0x1F601), S(0x1F601),
        ];

        foreach (string a in samples)
        {
            foreach (string b in samples)
            {
                int byBytes = Encoding.UTF8.GetBytes(a).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(b));
                Math.Sign(TagName.CompareBytes(a, b)).ShouldBe(Math.Sign(byBytes), $"U+{Hex(a)} against U+{Hex(b)}");
            }
        }

        // A pair of UTF-16 is below U+FF5E as numbers, and above it as bytes.
        string.CompareOrdinal(S(0x1F600), S(0xFF5E)).ShouldBeLessThan(0);
        TagName.CompareBytes(S(0x1F600), S(0xFF5E)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_node_holds_its_tags_in_the_order_it_got_them_whatever_order_a_script_reads_them_in()
    {
        TagSet tags = TagSet.Of("b", "a", "c").With("B");

        tags.ShouldBe(["b", "a", "c", "B"]);
        tags.Order(Comparer<string>.Create(TagName.CompareBytes)).ShouldBe(["B", "a", "b", "c"]);
    }

    private static string S(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static string Hex(string text) =>
        string.Join(" ", text.EnumerateRunes().Select(rune => rune.Value.ToString("X4")));
}
