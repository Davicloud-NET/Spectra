using System.Numerics;
using System.Text;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// One value of a member a script can set: how it prints, and when two are
/// the same.
/// </summary>
public sealed class MemberValueTests
{
    [Fact]
    public void A_value_prints_the_same_on_every_machine()
    {
        MemberValue.Of(new Vector3(0.1f, -0f, 1e20f)).ToString().ShouldBe("0.1, -0, 1E+20");
        MemberValue.Of("Door").ToString().ShouldBe("Door");
        MemberValue.Of(new CFrame(1f, 2f, 3f)).ToString().ShouldBe("1, 2, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1");
        MemberValue.None.ToString().ShouldBe("nil");
    }

    [Fact]
    public void Two_values_are_equal_when_they_are_of_one_kind_and_hold_the_same()
    {
        MemberValue.Of("Door").ShouldBe(MemberValue.Of("Door"));
        MemberValue.Of("Door").ShouldNotBe(MemberValue.Of("door"));
        MemberValue.Of(Vector3.One).ShouldNotBe(MemberValue.Of(new CFrame(Vector3.One)));
        MemberValue.Of(new CFrame(Vector3.One)).ShouldBe(MemberValue.Of(new CFrame(1f, 1f, 1f)));
    }

    [Fact]
    public void A_boolean_a_number_and_a_colour_print_as_a_script_writes_them_and_hold_what_they_were_made_from()
    {
        MemberValue.Of(true).ToString().ShouldBe("true");
        MemberValue.Of(0.1).ToString().ShouldBe("0.1");
        MemberValue.Of(new Color3(1f, 0.5f, -0f)).ToString().ShouldBe("1, 0.5, -0");

        MemberValue.Of(false).Boolean.ShouldBeFalse();
        MemberValue.Of(2.5).Number.ShouldBe(2.5);
        MemberValue.Of(new Color3(0.1f, 0.2f, 0.3f)).Color.ShouldBe(new Color3(0.1f, 0.2f, 0.3f));
        MemberValue.Of(1.0).ShouldNotBe(MemberValue.Of(true));
        MemberValue.Of(new Color3(1f, 1f, 1f)).ShouldNotBe(MemberValue.Of(Vector3.One));
    }

    [Fact]
    public void Every_kind_has_the_name_a_script_knows_it_by()
    {
        string[] names = [.. Enum.GetValues<MemberValueKind>().Select(MemberValue.TypeName)];

        names.ShouldBe(["nil", "string", "Vector3", "CFrame", "boolean", "number", "Color3", "Instance"]);
    }

    [Fact]
    public void A_node_is_a_value_and_so_is_no_node()
    {
        var door = new SceneNode("Door");

        MemberValue.OfNode(door).Node.ShouldBeSameAs(door);
        MemberValue.OfNode(door).ToString().ShouldBe(door.Id.ToString());
        MemberValue.OfNode(null).ToString().ShouldBe("nil");
        MemberValue.OfNode(door).ShouldBe(MemberValue.OfNode(door));
        MemberValue.OfNode(door).ShouldNotBe(MemberValue.OfNode(new SceneNode("Door")));
        MemberValue.OfNode(null).ShouldNotBe(MemberValue.None);
        MemberValue.OfNode(null).Kind.ShouldBe(MemberValueKind.Node);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    [InlineData("\u00e9\u20ac\U0001F600")]
    [InlineData("twenty-eight bytes of text..")]
    [InlineData("twenty-nine bytes of text....")]
    public void Text_from_a_string_and_from_its_UTF8_is_one_value(string text)
    {
        MemberValue fromString = MemberValue.Of(text);
        MemberValue fromBytes = MemberValue.OfUtf8(Encoding.UTF8.GetBytes(text));

        fromBytes.ShouldBe(fromString);
        fromString.ShouldBe(fromBytes);
        fromBytes.GetHashCode().ShouldBe(fromString.GetHashCode());
        fromBytes.Text.ShouldBe(text);
        fromBytes.ToString().ShouldBe(text);
        fromString.AsUtf8().IsUtf8.ShouldBeTrue();
        fromString.AsUtf8().Utf8.ToArray().ShouldBe(Encoding.UTF8.GetBytes(text));
        fromBytes.ShouldNotBe(MemberValue.Of(text + "x"));
    }

    [Fact]
    public void Long_text_from_a_string_and_from_its_UTF8_is_one_value()
    {
        string text = string.Concat(Enumerable.Repeat("\u00fc\U0001F600", 300));

        MemberValue.OfUtf8(Encoding.UTF8.GetBytes(text)).ShouldBe(MemberValue.Of(text));
        MemberValue.OfUtf8(Encoding.UTF8.GetBytes(text)).GetHashCode().ShouldBe(MemberValue.Of(text).GetHashCode());
        MemberValue.OfUtf8(Encoding.UTF8.GetBytes(text + "!")).ShouldNotBe(MemberValue.Of(text + "?"));
    }

    [Fact]
    public void Short_text_needs_no_array_and_longer_text_one_of_its_own_length()
    {
        byte[] shortest = new byte[MemberValue.InlineUtf8Bytes];
        byte[] longer = new byte[MemberValue.InlineUtf8Bytes + 1];
        MemberValue.OfUtf8(shortest);
        MemberValue.OfUtf8(longer);

        long before = GC.GetAllocatedBytesForCurrentThread();
        MemberValue kept = MemberValue.OfUtf8(shortest);
        long inline = GC.GetAllocatedBytesForCurrentThread() - before;
        MemberValue.OfUtf8(longer);
        long array = GC.GetAllocatedBytesForCurrentThread() - before - inline;

        inline.ShouldBe(0);
        array.ShouldBeInRange(longer.Length, longer.Length + 32);
        kept.Utf8.Length.ShouldBe(MemberValue.InlineUtf8Bytes);
    }

    [Fact]
    public void Bytes_that_are_no_UTF8_are_kept_as_they_are_and_equal_no_string()
    {
        byte[] bytes = [0xFF, 0x41, 0xC3];
        MemberValue value = MemberValue.OfUtf8(bytes);

        value.Utf8.ToArray().ShouldBe(bytes);
        value.ShouldBe(MemberValue.OfUtf8(bytes));
        value.ShouldNotBe(MemberValue.Of(value.Text));
    }

    [Fact]
    public void Numbers_are_equal_as_they_were_before_the_kinds_shared_their_bytes()
    {
        MemberValue.Of(0d).ShouldBe(MemberValue.Of(-0d));
        MemberValue.Of(double.NaN).ShouldBe(MemberValue.Of(double.NaN));
        MemberValue.Of(1d).ShouldNotBe(MemberValue.Of(true));
        MemberValue.Of(new Vector3(1f, 2f, 3f)).Number.ShouldBe(0d);
        MemberValue.Of(2.5).Vector.ShouldBe(Vector3.Zero);
        MemberValue.Of(new Vector3(1f, 2f, 3f)).Color.ShouldBe(new Color3(1f, 2f, 3f));
        MemberValue.Of(2.5).Frame.ShouldBe(default);
    }
}
