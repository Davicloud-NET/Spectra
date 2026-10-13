using SpectraEngine.Core;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A node's tags and attributes in a map: written on one line each in the
/// order they were added, absent when a node has none, read back as they
/// were, and refused when they break a rule a running level keeps.
/// </summary>
public sealed class MapNodeTagTests
{
    private static byte[] Utf8(string text) =>
        Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n") + "\n");

    // Hand-written. The unknown members pin where the two sit in a node.
    private const string Marked = """
        {
          "spectramap": 6,
          "minimumReadableVersion": 6,
          "engine": "1.0.0",
          "scene": {
            "name": "Marked"
          },
          "nodes": [
            {
              "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
              "name": "Brick",
              "transform": {"p":[0,0,0]},
              "script": {"path":"Scripts/kill.luau"},
              "afterScript": 1,
              "tags": ["Lava","Hot stuff","lava"],
              "afterTags": 2,
              "attributes": {"Damage":25,"Label":"hot \"lava\"","Deadly":true,"Safe":false,"Push":{"vector3":[0,12.5,-1]},"Glow":{"color3":[1,0.5,0]},"Rest":{"cframe":[1,2,3,0,0.70710677,0,0.70710677]},"Tenth":0.1,"Big":1E+300,"Whole":-3},
              "afterAttributes": 3,
              "editor": {"color":"red"},
              "children": []
            }
          ]
        }
        """;

    private static string OneNode(string members, int version = 6) => $$"""
        {
          "spectramap": {{version}},
          "minimumReadableVersion": 1,
          "engine": "1.0.0",
          "scene": {
            "name": "S"
          },
          "nodes": [
            {
              "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
              "name": "Suspect",
              "transform": {"p":[0,0,0]},
              {{members}}
              "children": []
            }
          ]
        }
        """;

    [Fact]
    public void Tags_and_attributes_round_trip_byte_for_byte_with_unknown_members_between_them()
    {
        byte[] source = Utf8(Marked);

        MapDocument document = MapReader.Read(source);

        MapNode brick = document.Nodes[0];
        brick.Tags.ShouldBe(["Lava", "Hot stuff", "lava"]);
        brick.Attributes.Select(pair => pair.Key).ShouldBe(
            ["Damage", "Label", "Deadly", "Safe", "Push", "Glow", "Rest", "Tenth", "Big", "Whole"]);
        brick.Unknown.Count.ShouldBe(3, "the two are decoded, never carried as unknown members");
        MapWriter.Write(document).ShouldBe(source);
    }

    [Fact]
    public void A_map_with_tags_and_attributes_saves_byte_for_byte_after_a_load()
    {
        // The same level with the members a scene has nowhere to keep left out.
        string[] kept = [.. Marked.ReplaceLineEndings("\n").Split('\n')
            .Where(line => !line.Contains("\"after") && !line.Contains("\"editor\""))];
        byte[] source = Utf8(string.Join("\n", kept));
        var loaded = new Scene("Marked");

        MapSceneBinder.ApplyTo(MapReader.Read(source), loaded);

        SceneNode brick = loaded.Root.Children[0];
        brick.Tags.ShouldBe(["Lava", "Hot stuff", "lava"]);
        ValueOf(brick, "Damage").ShouldBe(MemberValue.Of(25d));
        ValueOf(brick, "Label").ShouldBe(MemberValue.Of("hot \"lava\""));
        ValueOf(brick, "Deadly").ShouldBe(MemberValue.Of(true));
        ValueOf(brick, "Safe").ShouldBe(MemberValue.Of(false));
        ValueOf(brick, "Push").ShouldBe(MemberValue.Of(new Vector3(0f, 12.5f, -1f)));
        ValueOf(brick, "Glow").ShouldBe(MemberValue.Of(new Color3(1f, 0.5f, 0f)));
        ValueOf(brick, "Rest").Frame.Position.ShouldBe(new Vector3(1f, 2f, 3f));
        ValueOf(brick, "Rest").Frame.Rotation.ShouldBe(new Quaternion(0f, 0.70710677f, 0f, 0.70710677f));
        ValueOf(brick, "Tenth").Number.ShouldBe(0.1d);
        ValueOf(brick, "Big").Number.ShouldBe(1e300d);

        MapWriter.Write(MapSceneBinder.FromScene(loaded)).ShouldBe(source);
    }

    // A name is a JSON member's name, and JSON writes these three as they are.
    [Fact]
    public void A_name_with_a_period_a_hyphen_or_a_slash_is_a_member_like_any_other_and_keeps_its_bytes()
    {
        const string members =
            "\"attributes\": {\"a.b\":1,\"a-b\":\"x\",\"a/b\":true,\"ui/hud.title-2\":{\"vector3\":[1,2,3]},\"..\":2,\"9a\":3},";
        byte[] source = Utf8(OneNode(members));

        MapDocument document = MapReader.Read(source);

        document.Nodes[0].Attributes.Select(pair => pair.Key).ShouldBe(
            ["a.b", "a-b", "a/b", "ui/hud.title-2", "..", "9a"]);
        MapWriter.Write(document).ShouldBe(source);

        var loaded = new Scene("S");
        MapSceneBinder.ApplyTo(document, loaded);
        ValueOf(loaded.Root.Children[0], "a/b").ShouldBe(MemberValue.Of(true));
        ValueOf(loaded.Root.Children[0], "ui/hud.title-2").ShouldBe(MemberValue.Of(new Vector3(1f, 2f, 3f)));
        Encoding.UTF8.GetString(MapWriter.Write(MapSceneBinder.FromScene(loaded))).ShouldContain(members);
    }

    [Fact]
    public void A_slash_escaped_by_hand_is_the_same_name_and_is_saved_as_a_plain_slash()
    {
        byte[] byHand = Utf8(OneNode("\"attributes\": {\"a\\/b\":1},"));

        MapDocument document = MapReader.Read(byHand);

        document.Nodes[0].Attributes[0].Key.ShouldBe("a/b");
        Encoding.UTF8.GetString(MapWriter.Write(document)).ShouldContain("\"attributes\": {\"a/b\":1},");
    }

    [Fact]
    public void Every_kind_of_value_comes_back_from_a_save_as_it_was()
    {
        var turned = new CFrame(
            new Vector3(-4.25f, 0.1f, 1e-7f), Quaternion.CreateFromYawPitchRoll(0.7f, -1.1f, 2.3f));
        MemberValue[] values =
        [
            MemberValue.Of(""),
            MemberValue.Of($"two\nlines, a tab\t and a {(char)0xFC}"),
            MemberValue.Of(true),
            MemberValue.Of(false),
            MemberValue.Of(0d),
            MemberValue.Of(-0.1d),
            MemberValue.Of(1d / 3d),
            MemberValue.Of(double.MaxValue),
            MemberValue.Of(double.Epsilon),
            MemberValue.Of(9007199254740993d),
            MemberValue.Of(new Vector3(1f / 3f, -2.5f, 1e20f)),
            MemberValue.Of(new Color3(0.2f, 1.5f, -0.25f)),
            MemberValue.Of(CFrame.Identity),
            MemberValue.Of(turned),
        ];
        var source = new Scene("Values");
        SceneNode node = source.Root.CreateChild("Holder");
        AttributeSet set = AttributeSet.Empty;
        for (int i = 0; i < values.Length; i++)
            set = set.With($"Value{i}", values[i]);
        node.Attributes = set;

        byte[] bytes = MapWriter.Write(MapSceneBinder.FromScene(source));
        var loaded = new Scene("Values");
        MapSceneBinder.ApplyTo(MapReader.Read(bytes), loaded);

        AttributeSet back = loaded.Root.Children[0].Attributes;
        back.Count.ShouldBe(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            back[i].Key.ShouldBe($"Value{i}");
            back[i].Value.ShouldBe(values[i], $"Value{i}");
        }

        // Bit for bit: an equal frame could still be another quaternion.
        ValueOf(loaded.Root.Children[0], "Value13").Frame.Rotation.ShouldBe(turned.Rotation);
        MapWriter.Write(MapSceneBinder.FromScene(loaded)).ShouldBe(bytes);
    }

    [Fact]
    public void A_set_at_its_limits_comes_back_from_a_save()
    {
        var source = new Scene("Full");
        SceneNode node = source.Root.CreateChild("Holder");
        node.Tags = TagSet.Of([.. Enumerable.Range(0, TagLimits.MaxPerNode).Select(i => $"Tag {i}")])
            .Without("Tag 0").With(new string('t', TagLimits.MaxBytes));
        AttributeSet.TryMake(
            [.. Enumerable.Range(0, AttributeLimits.MaxPerNode).Select(i => new KeyValuePair<string, MemberValue>(
                i == 0 ? new string('n', AttributeName.MaxLength) : $"Name{i}",
                i == 1 ? MemberValue.Of(new string('x', AttributeLimits.MaxTextBytes)) : MemberValue.Of((double)i)))],
            out AttributeSet? full, out _).ShouldBeTrue();
        node.Attributes = full.ShouldNotBeNull();

        byte[] bytes = MapWriter.Write(MapSceneBinder.FromScene(source));
        var loaded = new Scene("Full");
        MapSceneBinder.ApplyTo(MapReader.Read(bytes), loaded);

        loaded.Root.Children[0].Tags.ShouldBe(node.Tags);
        loaded.Root.Children[0].Attributes.ShouldBe(node.Attributes);
    }

    [Fact]
    public void A_node_with_none_writes_neither_member_and_keeps_the_oldest_reader()
    {
        var source = new Scene("Plain");
        source.Root.CreateChild("Group").CreateChild("Marker");

        MapDocument document = MapSceneBinder.FromScene(source);
        string text = Encoding.UTF8.GetString(MapWriter.Write(document));

        text.ShouldNotContain("\"tags\"");
        text.ShouldNotContain("\"attributes\"");
        document.MinimumReadableVersion.ShouldBe(EngineInfo.MinimumReadableMapVersion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_scene_with_a_tag_or_an_attribute_demands_a_reader_that_keeps_them(bool isTag)
    {
        // An older editor carries both as members it does not know, and its
        // save drops them: it builds every node anew.
        var source = new Scene("Marked");
        SceneNode nested = source.Root.CreateChild("Group").CreateChild("Brick");
        if (isTag)
            nested.Tags = TagSet.Of("Lava");
        else
            nested.Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(25d));

        EngineInfo.NodeTagsMapVersion.ShouldBe(6);
        MapSceneBinder.FromScene(source).MinimumReadableVersion.ShouldBe(EngineInfo.NodeTagsMapVersion);
    }

    [Fact]
    public void A_map_from_before_tags_loads_with_none_and_saves_to_what_it_would_have()
    {
        // Version 5, as the editor wrote it before this member was there.
        byte[] old = Utf8(OneNode("\"script\": {\"path\":\"Scripts/kill.luau\"},", version: 5)
            .Replace("\"minimumReadableVersion\": 1", "\"minimumReadableVersion\": 5"));
        var loaded = new Scene("S");

        MapDocument document = MapReader.Read(old);
        MapSceneBinder.ApplyTo(document, loaded);

        loaded.Root.Children[0].Tags.Count.ShouldBe(0);
        loaded.Root.Children[0].Attributes.Count.ShouldBe(0);
        MapWriter.Write(document).ShouldBe(old, "read and written, an old file keeps its bytes");

        // A save stamps the engine's version and nothing else is new.
        string saved = Encoding.UTF8.GetString(MapWriter.Write(MapSceneBinder.FromScene(loaded)));
        saved.ShouldBe(Encoding.UTF8.GetString(old).Replace("\"spectramap\": 5", "\"spectramap\": 6"));
    }

    [Theory]
    [InlineData("\"tags\": \"Lava\",", "must be an array of strings")]
    [InlineData("\"tags\": [\"Lava\", 7],", "a tag must be a string")]
    [InlineData("\"tags\": [\"Lava\", \"\"],", "A tag cannot be empty")]
    [InlineData("\"tags\": [\"Lava\", \"Hot\", \"Lava\"],", "'Lava' is there twice")]
    [InlineData("\"attributes\": [1, 2],", "must be an object")]
    [InlineData("\"attributes\": {\"two words\": 1},", "'two words' is not a valid attribute name")]
    [InlineData("\"attributes\": {\"RBXHidden\": 1},", "does not start with RBX")]
    [InlineData("\"attributes\": {\"Damage\": 1, \"Damage\": 2},", "'Damage' is there twice")]
    [InlineData("\"attributes\": {\"Damage\": null},", "attribute 'Damage' must be a string, true or false, a number")]
    [InlineData("\"attributes\": {\"Damage\": [1, 2, 3]},", "attribute 'Damage' must be a string")]
    [InlineData("\"attributes\": {\"Damage\": {}},", "attribute 'Damage' must be a string")]
    [InlineData("\"attributes\": {\"Push\": {\"vector2\": [1, 2]}},", "not an object with 'vector2'")]
    [InlineData("\"attributes\": {\"Push\": {\"vector3\": [1, 2]}},", "'vector3' must be an array of 3 numbers")]
    [InlineData("\"attributes\": {\"Push\": {\"vector3\": [1, 2, 3, 4]}},", "'vector3' must be an array of 3 numbers")]
    [InlineData("\"attributes\": {\"Push\": {\"vector3\": [1, \"2\", 3]}},", "'vector3' must be an array of 3 numbers")]
    [InlineData("\"attributes\": {\"Glow\": {\"color3\": 1}},", "'color3' must be an array of 3 numbers")]
    [InlineData("\"attributes\": {\"Rest\": {\"cframe\": [1, 2, 3]}},", "'cframe' must be an array of 7 numbers")]
    [InlineData("\"attributes\": {\"Push\": {\"vector3\": [1, 2, 3], \"more\": 1}},", "'vector3' and nothing else")]
    [InlineData("\"tags\": [\"Lava\"], \"tags\": [\"Hot\"],", "'tags' is there twice")]
    [InlineData("\"tags\": [\"Lava\"], \"tags\": [],", "'tags' is there twice")]
    [InlineData("\"attributes\": {\"Damage\": 1}, \"attributes\": {\"Label\": \"hot\"},", "'attributes' is there twice")]
    [InlineData("\"attributes\": {\"Rest\": {\"cframe\": [1, 2, 3, 0, 0, 0, 0]}},", "the rotation in 'cframe' must be finite and have a length")]
    [InlineData("\"attributes\": {\"Rest\": {\"cframe\": [1, 2, 3, 1e39, 0, 0, 1]}},", "the rotation in 'cframe' must be finite and have a length")]
    [InlineData("\"attributes\": {\"Rest\": {\"cframe\": [1e39, 2, 3, 0, 0, 0, 1]}},", "number has to be finite")]
    public void What_breaks_a_rule_is_refused_by_the_nodes_name(string members, string words)
    {
        byte[] document = Utf8(OneNode(members));

        var thrown = Should.Throw<MapFormatException>(() => MapReader.Read(document));

        thrown.NodeName.ShouldBe("Suspect");
        thrown.ByteOffset.ShouldBeGreaterThan(0);
        thrown.Message.ShouldContain(words);
    }

    [Fact]
    public void A_frames_rotation_off_unit_length_is_the_same_rotation_and_is_saved_at_unit_length()
    {
        // By hand a quaternion is written to a few digits, or not brought to length at all.
        byte[] loose = Utf8(OneNode(
            "\"attributes\": {\"Half\":{\"cframe\":[1,2,3,0,0.7071,0,0.7071]},\"Long\":{\"cframe\":[0,0,0,0,0,0,2]}},"));

        MapDocument document = MapReader.Read(loose);

        Quaternion half = document.Nodes[0].Attributes[0].Value.Frame.Rotation;
        half.Length().ShouldBe(1f, 1e-6f);
        half.Y.ShouldBe(0.70710677f, 1e-6f);
        document.Nodes[0].Attributes[1].Value.Frame.Rotation.ShouldBe(Quaternion.Identity);

        // Once at unit length it is kept bit for bit.
        byte[] saved = MapWriter.Write(document);
        Encoding.UTF8.GetString(saved).ShouldContain("\"Half\":{\"cframe\":[1,2,3,0,0.70710677,0,0.70710677]}");
        Encoding.UTF8.GetString(saved).ShouldContain("\"Long\":{\"cframe\":[0,0,0,0,0,0,1]}");
        MapWriter.Write(MapReader.Read(saved)).ShouldBe(saved);
    }

    [Fact]
    public void A_tag_too_long_and_a_set_too_large_are_refused()
    {
        string tooLong = new('a', TagLimits.MaxBytes + 1);
        string tooMany = string.Join(",", Enumerable.Range(0, TagLimits.MaxPerNode + 1).Select(i => $"\"T{i}\""));
        string tooManyNames = string.Join(
            ",", Enumerable.Range(0, AttributeLimits.MaxPerNode + 1).Select(i => $"\"N{i}\": {i}"));

        Should.Throw<MapFormatException>(() => MapReader.Read(Utf8(OneNode($"\"tags\": [\"{tooLong}\"],"))))
            .Message.ShouldContain("257 bytes is too long");
        Should.Throw<MapFormatException>(() => MapReader.Read(Utf8(OneNode($"\"tags\": [{tooMany}],"))))
            .Message.ShouldContain("at most 256 tags");
        Should.Throw<MapFormatException>(
                () => MapReader.Read(Utf8(OneNode($"\"attributes\": {{{tooManyNames}}},"))))
            .Message.ShouldContain("at most 256 attributes");
    }

    [Fact]
    public void A_document_built_in_code_with_what_no_set_holds_is_refused_by_the_nodes_name()
    {
        var document = new MapDocument();
        var node = new MapNode { Id = Guid.NewGuid(), Name = "Built" };
        node.Tags.Add("Lava");
        node.Tags.Add("Lava");
        document.Nodes.Add(node);

        var thrown = Should.Throw<MapFormatException>(() => MapSceneBinder.ApplyTo(document, new Scene("S")));

        thrown.NodeName.ShouldBe("Built");
        thrown.Message.ShouldContain("'Lava' is there twice");
    }

    private static MemberValue ValueOf(SceneNode node, string name)
    {
        node.Attributes.TryGet(name, out MemberValue value).ShouldBeTrue(name);
        return value;
    }
}
