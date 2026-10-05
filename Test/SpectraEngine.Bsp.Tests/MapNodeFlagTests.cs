using SpectraEngine.Core;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The node flags in the map format: collide, query, touch and render, each
/// written only when off, so a map that carries none keeps its bytes.
/// </summary>
public sealed class MapNodeFlagTests
{
    private static byte[] Utf8(string text) =>
        Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n") + "\n");

    // Hand-written. The unknown members pin where each flag sits in a node.
    private const string Interleaved = """
        {
          "spectramap": 4,
          "minimumReadableVersion": 4,
          "engine": "1.0.0",
          "scene": {
            "name": "Flags"
          },
          "nodes": [
            {
              "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
              "name": "Trigger",
              "kind": "part",
              "afterKind": 1,
              "collide": false,
              "afterCollide": 2,
              "query": false,
              "afterQuery": 3,
              "touch": false,
              "afterTouch": 4,
              "render": false,
              "afterRender": 5,
              "transform": {"p":[0,0,0]},
              "afterTransform": 6,
              "children": []
            },
            {
              "id": "9c1e4d70-2a83-4f16-b5aa-0e6d3c8f21b7",
              "name": "Plain",
              "transform": {"p":[0,0,0]},
              "children": []
            }
          ]
        }
        """;

    // No brush: Brush re-normalises its planes, which changes the bytes.
    private const string Volumes = """
        {
          "spectramap": 4,
          "minimumReadableVersion": 4,
          "engine": "1.0.0",
          "scene": {
            "name": "Volumes"
          },
          "nodes": [
            {
              "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
              "name": "Trigger",
              "kind": "part",
              "collide": false,
              "query": false,
              "render": false,
              "transform": {"p":[0,1,0]},
              "children": [
                {
                  "id": "5b2f8a11-6c04-4e29-8d73-1af90b4e2c65",
                  "name": "Clip",
                  "kind": "part",
                  "touch": false,
                  "render": false,
                  "transform": {"p":[2,0,0]},
                  "children": []
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void The_flags_round_trip_byte_for_byte_with_unknown_members_between_them()
    {
        byte[] source = Utf8(Interleaved);

        MapWriter.Write(MapReader.Read(source)).ShouldBe(source);
    }

    [Fact]
    public void The_flags_decode_to_the_values_they_state_and_default_to_on()
    {
        MapDocument document = MapReader.Read(Utf8(Interleaved));

        MapNode trigger = document.Nodes[0];
        trigger.Collide.ShouldBeFalse();
        trigger.Query.ShouldBeFalse();
        trigger.Touch.ShouldBeFalse();
        trigger.Render.ShouldBeFalse();
        trigger.Unknown.Count.ShouldBe(6, "a flag must be decoded, never carried as an unknown member");

        MapNode plain = document.Nodes[1];
        plain.Collide.ShouldBeTrue();
        plain.Query.ShouldBeTrue();
        plain.Touch.ShouldBeTrue();
        plain.Render.ShouldBeTrue();
    }

    [Fact]
    public void A_flag_at_its_default_writes_nothing()
    {
        var document = new MapDocument();
        document.Nodes.Add(new MapNode { Id = Guid.NewGuid(), Name = "Plain" });

        string text = Encoding.UTF8.GetString(MapWriter.Write(document));

        text.ShouldNotContain("\"collide\"");
        text.ShouldNotContain("\"query\"");
        text.ShouldNotContain("\"touch\"");
        text.ShouldNotContain("\"render\"");
    }

    [Theory]
    // A flag read loosely would load a trigger volume as a solid, drawn block.
    [InlineData("render", "0")]
    [InlineData("collide", "\"false\"")]
    [InlineData("query", "null")]
    [InlineData("touch", "1")]
    public void A_flag_that_is_not_a_boolean_is_refused_with_the_nodes_name(string member, string value)
    {
        string text = $$"""
            {
              "spectramap": 4,
              "minimumReadableVersion": 4,
              "engine": "1.0.0",
              "scene": {
                "name": "S"
              },
              "nodes": [
                {
                  "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
                  "name": "Suspect",
                  "{{member}}": {{value}},
                  "transform": {"p":[0,0,0]},
                  "children": []
                }
              ]
            }
            """;

        var thrown = Should.Throw<MapFormatException>(() => MapReader.Read(Utf8(text)));

        thrown.NodeName.ShouldBe("Suspect");
        thrown.ByteOffset.ShouldBeGreaterThan(0);
        thrown.Message.ShouldContain(member);
    }

    [Theory]
    [InlineData("collide")]
    [InlineData("query")]
    [InlineData("touch")]
    [InlineData("render")]
    public void Each_flag_survives_a_scene_round_trip_on_its_own(string flag)
    {
        var source = new Scene("Flags");
        SceneNode node = source.Root.CreateChild("Volume");
        node.BrushKind = BrushKind.Part;
        node.Brush = Box();
        TurnOff(node, flag);

        byte[] bytes = MapWriter.Write(MapSceneBinder.FromScene(source));
        var loaded = new Scene("Flags");
        MapSceneBinder.ApplyTo(MapReader.Read(bytes), loaded);

        SceneNode back = loaded.Root.Children[0];
        back.CanCollide.ShouldBe(flag != "collide");
        back.CanQuery.ShouldBe(flag != "query");
        back.CanTouch.ShouldBe(flag != "touch");
        back.IsRendered.ShouldBe(flag != "render");
        back.Anchored.ShouldBeTrue("loading a flag must not disturb the bits the map does not carry");
        Encoding.UTF8.GetString(bytes).ShouldContain($"\"{flag}\": false");
    }

    [Fact]
    public void A_scene_with_every_flag_on_writes_none_of_them_and_keeps_the_oldest_reader()
    {
        var source = new Scene("Plain");
        source.Root.CreateChild("Wall").Brush = Box();

        MapDocument document = MapSceneBinder.FromScene(source);
        string text = Encoding.UTF8.GetString(MapWriter.Write(document));

        document.MinimumReadableVersion.ShouldBe(EngineInfo.MinimumReadableMapVersion);
        text.ShouldNotContain("\"collide\"");
        text.ShouldNotContain("\"query\"");
        text.ShouldNotContain("\"touch\"");
        text.ShouldNotContain("\"render\"");
    }

    [Theory]
    [InlineData("collide")]
    [InlineData("query")]
    [InlineData("touch")]
    [InlineData("render")]
    public void A_scene_carrying_a_flag_demands_a_reader_that_can_keep_it(string flag)
    {
        // An older editor would load the volume solid and drawn, and save it so.
        var source = new Scene("Flags");
        SceneNode nested = source.Root.CreateChild("Group").CreateChild("Volume");
        TurnOff(nested, flag);

        MapSceneBinder.FromScene(source).MinimumReadableVersion.ShouldBe(EngineInfo.NodeFlagsMapVersion);
    }

    [Fact]
    public void A_map_of_volumes_saves_byte_for_byte_after_a_load()
    {
        byte[] source = Utf8(Volumes);
        var loaded = new Scene("Testmap");

        MapSceneBinder.ApplyTo(MapReader.Read(source), loaded);

        MapWriter.Write(MapSceneBinder.FromScene(loaded)).ShouldBe(source);
    }

    [Fact]
    public void A_part_loaded_with_render_off_is_hidden_and_never_gets_a_mesh()
    {
        var source = new Scene("Flags");
        SceneNode trigger = source.Root.CreateChild("Trigger");
        trigger.BrushKind = BrushKind.Part;
        trigger.Brush = Box();
        trigger.IsRendered = false;

        var loaded = new Scene("Flags");
        MapSceneBinder.ApplyTo(MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), loaded);
        var renderer = new FakeRenderer();
        loaded.ProcessPartBrushMeshes(renderer);

        loaded.HiddenBrushNodes.ShouldBe([loaded.Root.Children[0]]);
        renderer.CreatedMeshes.ShouldBeEmpty();
    }

    private static Brush Box() => Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

    private static void TurnOff(SceneNode node, string flag)
    {
        switch (flag)
        {
            case "collide": node.CanCollide = false; break;
            case "query": node.CanQuery = false; break;
            case "touch": node.CanTouch = false; break;
            case "render": node.IsRendered = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(flag), flag, "Not a node flag.");
        }
    }
}
