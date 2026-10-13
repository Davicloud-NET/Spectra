using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The tags and attributes a node is built with: two sets that never change
/// once made, and that hold nothing the rules of a running level refuse.
/// </summary>
public sealed class NodeTagSetTests
{
    [Fact]
    public void A_node_starts_with_no_tag_and_no_attribute()
    {
        var node = new SceneNode("Brick");

        node.Tags.ShouldBeSameAs(TagSet.Empty);
        node.Attributes.ShouldBeSameAs(AttributeSet.Empty);
    }

    [Fact]
    public void Tags_keep_the_order_they_were_added_in_and_hold_none_twice()
    {
        TagSet tags = TagSet.Empty.With("Lava").With("Hot").With("A tag with spaces");

        tags.ShouldBe(["Lava", "Hot", "A tag with spaces"]);
        tags.With("Hot").ShouldBeSameAs(tags);
        tags.Without("Lava").ShouldBe(["Hot", "A tag with spaces"]);
        tags.Without("Cold").ShouldBeSameAs(tags);
        TagSet.Of("Lava").Without("Lava").ShouldBeSameAs(TagSet.Empty);
    }

    [Fact]
    public void A_tag_is_case_sensitive()
    {
        TagSet tags = TagSet.Of("Lava", "lava");

        tags.Count.ShouldBe(2);
        tags.Contains("LAVA").ShouldBeFalse();
    }

    [Fact]
    public void Two_sets_of_the_same_tags_in_the_same_order_are_equal()
    {
        TagSet.Of("Lava", "Hot").ShouldBe(TagSet.Of("Lava", "Hot"));
        TagSet.Of("Lava", "Hot").Equals(TagSet.Of("Hot", "Lava")).ShouldBeFalse();
    }

    [Fact]
    public void A_tag_of_256_bytes_is_kept_and_one_byte_more_is_refused()
    {
        string longest = new('a', TagLimits.MaxBytes);

        TagSet.Empty.With(longest).Count.ShouldBe(1);
        Should.Throw<ArgumentException>(() => TagSet.Empty.With(longest + "a"))
            .Message.ShouldContain("257 bytes");

        // Bytes, not characters: this one is two bytes each.
        string wide = new((char)0xFC, (TagLimits.MaxBytes / 2) + 1);
        TagName.IsValid(wide).ShouldBeFalse();
    }

    // The second has half of a surrogate pair in it, by its code point.
    public static TheoryData<int> RefusedTags => [0, 0xD83D];

    [Theory]
    [MemberData(nameof(RefusedTags))]
    public void A_tag_that_is_empty_or_no_text_is_refused(int codePoint)
    {
        string tag = codePoint == 0 ? "" : $"half {(char)codePoint} of a pair";

        TagName.IsValid(tag).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => TagSet.Empty.With(tag));
        TagSet.TryMake([tag], out _, out string? refusal).ShouldBeFalse();
        refusal.ShouldNotBeNull().ShouldEndWith(".");
    }

    [Fact]
    public void A_node_has_256_tags_and_no_more()
    {
        string[] names = [.. Enumerable.Range(0, TagLimits.MaxPerNode).Select(i => $"Tag{i}")];
        TagSet full = TagSet.Of(names);

        full.IsFull.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => full.With("OneMore")).Message.ShouldContain("at most 256 tags");
        TagSet.TryMake([.. names, "OneMore"], out _, out string? refusal).ShouldBeFalse();
        refusal.ShouldNotBeNull().ShouldContain("257");
    }

    [Fact]
    public void A_tag_named_twice_makes_no_set()
    {
        TagSet.TryMake(["Lava", "Hot", "Lava"], out _, out string? refusal).ShouldBeFalse();

        refusal.ShouldBe("The tag 'Lava' is there twice.");
    }

    [Fact]
    public void Attributes_keep_the_order_they_were_added_in_and_a_new_value_keeps_its_place()
    {
        AttributeSet set = AttributeSet.Empty
            .With("Damage", MemberValue.Of(25d))
            .With("Label", MemberValue.Of("hot"))
            .With("Deadly", MemberValue.Of(true));

        AttributeSet changed = set.With("Label", MemberValue.Of("cold"));

        changed.Select(pair => pair.Key).ShouldBe(["Damage", "Label", "Deadly"]);
        changed.TryGet("Label", out MemberValue label).ShouldBeTrue();
        label.ShouldBe(MemberValue.Of("cold"));
        set.TryGet("Label", out MemberValue before).ShouldBeTrue();
        before.ShouldBe(MemberValue.Of("hot"), "the set an edit started from is as it was");
        set.With("Damage", MemberValue.Of(25d)).ShouldBeSameAs(set);
        set.TryGet("damage", out _).ShouldBeFalse("a name is case sensitive");
    }

    [Fact]
    public void An_attribute_holds_each_of_the_six_kinds_of_value()
    {
        var frame = new CFrame(new Vector3(1f, 2f, 3f), Quaternion.CreateFromYawPitchRoll(0.5f, 0.25f, 0f));
        AttributeSet set = AttributeSet.Empty
            .With("Text", MemberValue.Of("hot"))
            .With("Flag", MemberValue.Of(false))
            .With("Number", MemberValue.Of(0.1d))
            .With("Push", MemberValue.Of(new Vector3(0f, 12.5f, 0f)))
            .With("Glow", MemberValue.Of(new Color3(1f, 0.5f, 0f)))
            .With("Rest", MemberValue.Of(frame));

        set.Select(pair => pair.Value.Kind).ShouldBe(
        [
            MemberValueKind.Text, MemberValueKind.Boolean, MemberValueKind.Number,
            MemberValueKind.Vector3, MemberValueKind.Color, MemberValueKind.CFrame,
        ]);
        set.TryGet("Rest", out MemberValue rest).ShouldBeTrue();
        rest.Frame.ShouldBe(frame);
    }

    [Fact]
    public void An_attribute_taken_out_or_renamed_leaves_the_rest_in_place()
    {
        AttributeSet set = AttributeSet.Empty
            .With("A", MemberValue.Of(1d)).With("B", MemberValue.Of(2d)).With("C", MemberValue.Of(3d));

        set.Without("B").Select(pair => pair.Key).ShouldBe(["A", "C"]);
        set.Without("Z").ShouldBeSameAs(set);
        set.Renamed("B", "Middle").Select(pair => pair.Key).ShouldBe(["A", "Middle", "C"]);
        set.Renamed("Z", "Other").ShouldBeSameAs(set);
        Should.Throw<ArgumentException>(() => set.Renamed("B", "C")).Message.ShouldContain("'C' already");
        Should.Throw<ArgumentException>(() => set.Renamed("B", "not a name"));
    }

    // The last has a letter outside ASCII, by its code point.
    public static TheoryData<string> RefusedNames => ["", "two words", "RBXHidden", $"caf{(char)0xE9}"];

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public void A_name_the_rule_of_scripts_refuses_is_refused_here_too(string name)
    {
        Should.Throw<ArgumentException>(() => AttributeSet.Empty.With(name, MemberValue.Of(1d)));
        AttributeSet.TryMake([new(name, MemberValue.Of(1d))], out _, out string? refusal).ShouldBeFalse();
        refusal.ShouldNotBeNull().ShouldContain(AttributeName.Rule);
    }

    [Fact]
    public void A_value_a_map_cannot_write_is_refused()
    {
        MemberValue[] refused =
        [
            MemberValue.None,
            MemberValue.Of(double.NaN),
            MemberValue.Of(double.PositiveInfinity),
            MemberValue.Of(new Vector3(0f, float.NaN, 0f)),
            MemberValue.Of(new Color3(float.PositiveInfinity, 0f, 0f)),
            MemberValue.Of(new CFrame(new Vector3(float.NegativeInfinity, 0f, 0f))),
            MemberValue.Of($"half {(char)0xD83D} of a pair"),
            MemberValue.Of(new string('a', AttributeLimits.MaxTextBytes + 1)),
        ];

        foreach (MemberValue value in refused)
        {
            AttributeValueRule.Refusal(in value).ShouldNotBeNull(value.ToString());
            Should.Throw<ArgumentException>(() => AttributeSet.Empty.With("Value", value));
        }

        MemberValue longest = MemberValue.Of(new string('a', AttributeLimits.MaxTextBytes));
        AttributeSet.Empty.With("Value", longest).Count.ShouldBe(1);
    }

    [Fact]
    public void A_node_holds_256_attributes_and_no_more()
    {
        var pairs = Enumerable.Range(0, AttributeLimits.MaxPerNode)
            .Select(i => new KeyValuePair<string, MemberValue>($"Name{i}", MemberValue.Of((double)i)))
            .ToList();
        AttributeSet.TryMake(pairs, out AttributeSet? full, out _).ShouldBeTrue();

        full.ShouldNotBeNull().IsFull.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => full.With("OneMore", MemberValue.Of(1d)))
            .Message.ShouldContain("at most 256 attributes");
        full.With("Name7", MemberValue.Of(700d)).Count.ShouldBe(256, "a name it has takes a new value");

        pairs.Add(new("OneMore", MemberValue.Of(1d)));
        AttributeSet.TryMake(pairs, out _, out string? refusal).ShouldBeFalse();
        refusal.ShouldNotBeNull().ShouldContain("257");
    }

    [Fact]
    public void A_copy_of_a_node_has_its_tags_and_attributes()
    {
        var parent = new SceneNode("Group");
        SceneNode brick = parent.CreateChild("Brick");
        brick.Tags = TagSet.Of("Lava");
        brick.Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(25d));

        SceneNode copy = parent.Clone();

        copy.Children[0].Tags.ShouldBe(["Lava"]);
        copy.Children[0].Attributes.ShouldBe(brick.Attributes);
        copy.Tags.Count.ShouldBe(0);
    }

    [Fact]
    public void Other_tags_on_a_node_of_a_scene_raise_the_tags_event_and_the_same_tags_do_not()
    {
        var scene = new Scene("Level");
        SceneNode brick = scene.Root.CreateChild("Brick");
        var raised = new List<SceneNode>();
        scene.NodeTagsChanged += raised.Add;

        brick.Tags = TagSet.Of("Lava");
        brick.Tags = TagSet.Of("Lava");
        brick.Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(25d));
        new SceneNode("Loose").Tags = TagSet.Of("Lava");

        raised.ShouldBe([brick]);
    }
}
