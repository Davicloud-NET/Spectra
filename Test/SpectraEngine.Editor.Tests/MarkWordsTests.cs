using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The words the Tags and Attributes sections say to a builder. The rules
/// behind them count bytes, which nobody at a panel can.
/// </summary>
public sealed class MarkWordsTests
{
    [Fact]
    public void The_kind_tooltip_says_what_a_script_reads_each_kind_as()
    {
        foreach (string label in AttributeKindWords.Labels)
            AttributeKindWords.Help.ShouldContain($"{label} as a ", Case.Sensitive);
    }

    [Fact]
    public void A_tag_too_long_is_refused_in_letters_and_not_in_bytes()
    {
        string? words = PropertyLimits.TagRefusal(new string('a', TagLimits.MaxBytes + 1));

        words.ShouldBe("This tag is too long. A tag has room for 256 letters, or fewer with accents and symbols.");
        PropertyLimits.TagRefusal(new string('a', TagLimits.MaxBytes)).ShouldBeNull();

        // Two bytes each, so half as many fit.
        PropertyLimits.TagRefusal(new string((char)0xFC, 129)).ShouldBe(words);
    }

    [Fact]
    public void Text_too_long_for_an_attribute_is_refused_in_letters_and_not_in_bytes()
    {
        MemberValue tooLong = MemberValue.Of(new string('x', AttributeLimits.MaxTextBytes + 1));

        PropertyLimits.AttributeValueRefusal(in tooLong).ShouldBe(
            "This text is too long. An attribute has room for 65536 letters, or fewer with accents and symbols.");
    }

    [Fact]
    public void Half_of_a_pair_of_characters_is_refused_as_one_that_cannot_be_saved()
    {
        string broken = "hot" + (char)0xD83D;
        MemberValue text = MemberValue.Of(broken);

        PropertyLimits.TagRefusal(broken).ShouldBe("This tag has a character that cannot be saved.");
        PropertyLimits.AttributeValueRefusal(in text).ShouldBe("This text has a character that cannot be saved.");
    }

    [Fact]
    public void A_number_and_a_tag_that_keep_the_rules_are_not_refused()
    {
        MemberValue number = MemberValue.Of(25d);
        MemberValue empty = MemberValue.Of("");

        PropertyLimits.AttributeValueRefusal(in number).ShouldBeNull();
        PropertyLimits.AttributeValueRefusal(in empty).ShouldBeNull();
        PropertyLimits.TagRefusal("Hot stuff").ShouldBeNull();
        PropertyLimits.TagRefusal("").ShouldBe("A tag cannot be empty.");
    }

    [Fact]
    public void A_tag_typed_too_long_at_the_panel_shows_the_plain_words()
    {
        var rig = new TagPanelRig(null, new SceneNode("Brick"));

        rig.Type(rig.Tags.AddField, new string('a', TagLimits.MaxBytes + 1));

        rig.Tags.AddField.Rejection.ShouldNotContain("bytes");
        rig.Tags.AddField.Rejection.ShouldStartWith("Not added: This tag is too long.");
    }
}
