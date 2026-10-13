using SpectraEngine.Core;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The version a save stamps and the reader a map asks for: a map asks for
/// the newest thing it holds, a map stamped with an earlier version still
/// loads, and one that asks for more than this build reads is refused by its
/// version.
/// </summary>
public sealed class MapFormatFloorTests
{
    private const string Stamp = "\"spectramap\": 7";

    // Hand-written, as format 6 saved it: such a name under a floor of 6.
    private const string SavedByFormat6 = """
        {
          "spectramap": 6,
          "minimumReadableVersion": 6,
          "engine": "1.0.0",
          "scene": {
            "name": "S"
          },
          "nodes": [
            {
              "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
              "name": "Brick",
              "transform": {"p":[0,0,0]},
              "attributes": {"Damage":25,"ui/hud.title-2":true},
              "children": []
            }
          ]
        }
        """;

    private static string Saved(Scene scene) =>
        Encoding.UTF8.GetString(MapWriter.Write(MapSceneBinder.FromScene(scene)));

    private static Scene Loaded(string map)
    {
        var scene = new Scene("S");
        MapSceneBinder.ApplyTo(MapReader.Read(Encoding.UTF8.GetBytes(map)), scene);
        return scene;
    }

    private static Scene WithAttribute(string name)
    {
        var scene = new Scene("S");
        scene.Root.CreateChild("Group").CreateChild("Brick").Attributes =
            AttributeSet.Empty.With("Damage", MemberValue.Of(25d)).With(name, MemberValue.Of(true));
        return scene;
    }

    // The newest thing a map of each format could hold.
    private static Scene HoldingWhatCameWith(int version)
    {
        var scene = new Scene("S");
        SceneNode node = scene.Root.CreateChild("Group").CreateChild("Thing");
        switch (version)
        {
            case 1: node.Light = new Light { Kind = LightKind.Point }; break;
            case 2: node.Light = new Light { Kind = LightKind.Spot }; break;
            case 3: node.Entity = new EntityData("func_door"); break;
            case 4: node.CanCollide = false; break;
            case 5: node.Script = new Script("Scripts/door.luau"); break;
            case 6: node.Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(25d)); break;
        }

        return scene;
    }

    [Fact]
    public void A_save_stamps_format_7()
    {
        EngineInfo.MapFormatVersion.ShouldBe(7);
        Saved(new Scene("Empty")).ShouldContain(Stamp);
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("a-b")]
    [InlineData("a/b")]
    [InlineData("ui/hud.title-2")]
    public void A_name_with_a_period_a_hyphen_or_a_slash_asks_for_a_reader_of_7_and_keeps_its_bytes(string name)
    {
        string saved = Saved(WithAttribute(name));

        EngineInfo.AttributeNameMapVersion.ShouldBe(7);
        saved.ShouldContain("\"minimumReadableVersion\": 7");
        saved.ShouldContain($"\"{name}\":true");
        Saved(Loaded(saved)).ShouldBe(saved);
    }

    [Theory]
    [InlineData("Damage2")]
    [InlineData("damage_2")]
    [InlineData("9a")]
    public void A_name_an_older_reader_takes_asks_for_no_more_than_attributes_do(string name)
    {
        string saved = Saved(WithAttribute(name));

        saved.ShouldContain("\"minimumReadableVersion\": 6");
        Saved(Loaded(saved)).ShouldBe(saved);
    }

    [Fact]
    public void A_tag_with_a_period_asks_for_no_more_than_tags_do()
    {
        var scene = new Scene("S");
        scene.Root.CreateChild("Brick").Tags = TagSet.Of("ui/hud.title-2");

        MapSceneBinder.FromScene(scene).MinimumReadableVersion.ShouldBe(EngineInfo.NodeTagsMapVersion);
    }

    [Fact]
    public void A_map_saved_with_such_a_name_and_a_floor_of_6_loads_and_its_next_save_asks_for_7()
    {
        string before = SavedByFormat6.ReplaceLineEndings("\n") + "\n";
        byte[] beforeBytes = Encoding.UTF8.GetBytes(before);
        string after = before
            .Replace("\"spectramap\": 6", Stamp)
            .Replace("\"minimumReadableVersion\": 6", "\"minimumReadableVersion\": 7");

        MapWriter.Write(MapReader.Read(beforeBytes)).ShouldBe(beforeBytes, "read and written, it keeps its bytes");
        Saved(Loaded(before)).ShouldBe(after);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void A_map_stamped_with_each_earlier_version_loads_to_the_same_nodes(int version)
    {
        string now = Saved(HoldingWhatCameWith(version));
        now.ShouldContain($"\"minimumReadableVersion\": {version}");
        string old = now.Replace(Stamp, $"\"spectramap\": {version}");
        byte[] oldBytes = Encoding.UTF8.GetBytes(old);

        MapDocument document = MapReader.Read(oldBytes);

        document.FormatVersion.ShouldBe(version);
        MapWriter.Write(document).ShouldBe(oldBytes, "read and written, the file keeps its stamp");
        Saved(Loaded(old)).ShouldBe(now, "a save stamps the engine's version and nothing else is new");
    }

    [Fact]
    public void A_map_that_asks_for_a_reader_of_8_is_refused_by_its_version()
    {
        string tooNew = Saved(WithAttribute("a.b"))
            .Replace(Stamp, "\"spectramap\": 8")
            .Replace("\"minimumReadableVersion\": 7", "\"minimumReadableVersion\": 8");

        var thrown = Should.Throw<MapFormatException>(() => MapReader.Read(Encoding.UTF8.GetBytes(tooNew)));

        thrown.Message.ShouldContain("This map needs a reader for map format 8; this engine implements 7.");
        thrown.Message.ShouldNotContain("attribute");
    }

    [Fact]
    public void A_map_stamped_8_that_asks_for_a_reader_of_7_is_read()
    {
        string saved = Saved(WithAttribute("a.b"));
        saved.ShouldContain("\"minimumReadableVersion\": 7");
        string newer = saved.Replace(Stamp, "\"spectramap\": 8");

        Loaded(newer).Root.Children[0].Children[0].Attributes.Contains("a.b").ShouldBeTrue();
    }
}
