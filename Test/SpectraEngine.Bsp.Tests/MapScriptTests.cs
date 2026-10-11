using SpectraEngine.Core;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The script payload in the map format: a node's script member names a file
/// by its content path, and a map with scripts keeps its bytes through a save.
/// </summary>
public sealed class MapScriptTests
{
    private static byte[] Utf8(string text) =>
        Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n") + "\n");

    private static int IndexOf(byte[] document, string token) =>
        Encoding.UTF8.GetString(document).IndexOf(token, StringComparison.Ordinal);

    // Hand-written. The unknown members pin where 'script' sits in a node and
    // where each member sits in the record.
    private const string Interleaved = """
        {
          "spectramap": 5,
          "minimumReadableVersion": 5,
          "engine": "1.0.0",
          "scene": {
            "name": "Scripts"
          },
          "nodes": [
            {
              "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
              "name": "Door",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "func_door"
              },
              "afterEntity": 1,
              "script": {"first":0,"path":"Scripts/door.luau","afterPath":1,"enabled":false,"afterEnabled":2,"module":true,"afterModule":3},
              "afterScript": 2,
              "editor": {"folded":true},
              "children": []
            },
            {
              "id": "9c1e4d70-2a83-4f16-b5aa-0e6d3c8f21b7",
              "name": "Plain",
              "transform": {"p":[0,0,0]},
              "script": {"path":"Scripts/plain.luau"},
              "children": []
            }
          ]
        }
        """;

    // No brush: Brush re-normalises its planes, which changes the bytes.
    private const string Level = """
        {
          "spectramap": 5,
          "minimumReadableVersion": 5,
          "engine": "1.0.0",
          "scene": {
            "name": "Testmap"
          },
          "nodes": [
            {
              "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
              "name": "Door",
              "kind": "part",
              "transform": {"p":[0,1,0]},
              "children": [
                {
                  "id": "5b2f8a11-6c04-4e29-8d73-1af90b4e2c65",
                  "name": "Opener",
                  "transform": {"p":[0,0,0]},
                  "script": {"path":"Scripts/door.luau"},
                  "children": []
                },
                {
                  "id": "9c1e4d70-2a83-4f16-b5aa-0e6d3c8f21b7",
                  "name": "Shared",
                  "transform": {"p":[0,0,0]},
                  "script": {"path":"Scripts/Lib/shared.luau","enabled":false,"module":true},
                  "children": []
                }
              ]
            }
          ]
        }
        """;

    private static byte[] NodeWith(string member) => Utf8($$"""
        {
          "spectramap": 5,
          "minimumReadableVersion": 5,
          "engine": "1.0.0",
          "scene": {
            "name": "S"
          },
          "nodes": [
            {
              "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
              "name": "Suspect",
              "transform": {"p":[0,0,0]},
              {{member}},
              "children": []
            }
          ]
        }
        """);

    [Fact]
    public void The_script_member_round_trips_byte_for_byte_with_unknown_members_around_it()
    {
        byte[] source = Utf8(Interleaved);

        MapWriter.Write(MapReader.Read(source)).ShouldBe(source);
    }

    [Fact]
    public void The_script_member_decodes_to_what_it_states_and_defaults_to_on_and_no_module()
    {
        MapDocument document = MapReader.Read(Utf8(Interleaved));

        MapScript door = document.Nodes[0].Script.ShouldNotBeNull();
        door.Path.ShouldBe("Scripts/door.luau");
        door.Enabled.ShouldBeFalse();
        door.Module.ShouldBeTrue();
        door.Unknown.Count.ShouldBe(4);
        document.Nodes[0].Unknown.Select(member => member.Name).ShouldBe(
            ["afterEntity", "afterScript"], "a script must be decoded, never carried as an unknown member");

        MapScript plain = document.Nodes[1].Script.ShouldNotBeNull();
        plain.Path.ShouldBe("Scripts/plain.luau");
        plain.Enabled.ShouldBeTrue();
        plain.Module.ShouldBeFalse();
    }

    [Fact]
    public void A_script_at_its_defaults_writes_only_its_path()
    {
        var document = new MapDocument();
        document.Nodes.Add(new MapNode
        {
            Id = Guid.NewGuid(),
            Name = "Opener",
            Script = new MapScript { Path = "Scripts/door.luau" },
        });

        string text = Encoding.UTF8.GetString(MapWriter.Write(document));

        text.ShouldContain("""
            "script": {"path":"Scripts/door.luau"},
            """.Trim());
    }

    [Fact]
    public void A_node_with_no_script_writes_no_script_member()
    {
        var document = new MapDocument();
        document.Nodes.Add(new MapNode { Id = Guid.NewGuid(), Name = "Plain" });

        Encoding.UTF8.GetString(MapWriter.Write(document)).ShouldNotContain("\"script\"");
    }

    [Theory]
    [InlineData("""
        "script": "Scripts/door.luau"
        """, "must be an object")]
    [InlineData("""
        "script": {}
        """, "path")]
    [InlineData("""
        "script": {"module":true}
        """, "path")]
    [InlineData("""
        "script": {"path":7}
        """, "path")]
    [InlineData("""
        "script": {"path":""}
        """, "content root")]
    [InlineData("""
        "script": {"path":"../door.luau"}
        """, "../door.luau")]
    [InlineData("""
        "script": {"path":"C:/Game/door.luau"}
        """, "C:/Game/door.luau")]
    [InlineData("""
        "script": {"path":"Scripts/door.luau","enabled":0}
        """, "enabled")]
    [InlineData("""
        "script": {"path":"Scripts/door.luau","module":"yes"}
        """, "module")]
    public void A_bad_script_member_is_refused_with_the_node_and_the_place(string member, string says)
    {
        byte[] document = NodeWith(member.Trim());
        int start = IndexOf(document, "\"script\"");

        var thrown = Should.Throw<MapFormatException>(() => MapReader.Read(document));

        thrown.NodeName.ShouldBe("Suspect");
        thrown.Message.ShouldContain(says);
        thrown.ByteOffset.ShouldBeInRange(start, start + member.Trim().Length);
    }

    [Theory]
    [InlineData("""
        "script": {"module":false,"source":["local part = script.Parent"]}
        """)]
    [InlineData("""
        "script": {"path":"Scripts/door.luau","source":["print(1)"]}
        """)]
    public void A_script_written_inside_the_map_is_refused_and_the_error_says_where(string member)
    {
        // Kept as an unknown member, the next save would delete the script's code.
        byte[] document = NodeWith(member.Trim());

        var thrown = Should.Throw<MapFormatException>(() => MapReader.Read(document));

        thrown.NodeName.ShouldBe("Suspect");
        thrown.Message.ShouldContain("Inline source is not supported yet");
        thrown.Message.ShouldContain("'path'");
        thrown.ByteOffset.ShouldBe(IndexOf(document, "\"source\""));
    }

    [Fact]
    public void A_scene_with_scripts_saved_loaded_and_saved_again_gives_the_same_bytes()
    {
        var source = new Scene("Level");
        SceneNode door = source.Root.CreateChild("Door");
        door.BrushKind = BrushKind.Part;
        door.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        door.CreateChild("Opener").Script = new Script("Scripts/door.luau");
        door.CreateChild("Shared").Script =
            new Script(@"Scripts\Lib\shared.luau") { IsEnabled = false, IsModule = true };
        source.Root.CreateChild("Rules").Script = new Script("Scripts/rules.luau") { IsEnabled = false };

        byte[] first = MapWriter.Write(MapSceneBinder.FromScene(source));
        var loaded = new Scene("Level");
        MapSceneBinder.ApplyTo(MapReader.Read(first), loaded);
        byte[] second = MapWriter.Write(MapSceneBinder.FromScene(loaded));

        second.ShouldBe(first);
        loaded.ScriptNodes.Select(node => node.Name).ShouldBe(["Opener", "Shared", "Rules"]);
        loaded.ScriptNodes[0].Script.ShouldBe(new Script("Scripts/door.luau"));
        loaded.ScriptNodes[1].Script.ShouldBe(
            new Script("Scripts/Lib/shared.luau") { IsEnabled = false, IsModule = true });
        loaded.ScriptNodes[2].Script.ShouldBe(new Script("Scripts/rules.luau") { IsEnabled = false });
        loaded.ScriptNodes[0].Parent.ShouldNotBeNull().Name.ShouldBe("Door");
    }

    [Fact]
    public void A_map_with_scripts_saves_byte_for_byte_after_a_load()
    {
        byte[] source = Utf8(Level);
        var loaded = new Scene("Testmap");

        MapSceneBinder.ApplyTo(MapReader.Read(source), loaded);

        loaded.ScriptNodes.Count.ShouldBe(2);
        MapWriter.Write(MapSceneBinder.FromScene(loaded)).ShouldBe(source);
    }

    [Fact]
    public void A_path_is_kept_as_written_by_the_document_and_saved_normalized_through_a_scene()
    {
        byte[] source = NodeWith("""
            "script": {"path":"Scripts\\door.luau"}
            """.Trim());

        MapDocument document = MapReader.Read(source);
        document.Nodes[0].Script.ShouldNotBeNull().Path.ShouldBe(@"Scripts\door.luau");
        MapWriter.Write(document).ShouldBe(source);

        var loaded = new Scene("S");
        MapSceneBinder.ApplyTo(document, loaded);
        loaded.Root.Children[0].Script.ShouldNotBeNull().Path.ShouldBe("Scripts/door.luau");
        Encoding.UTF8.GetString(MapWriter.Write(MapSceneBinder.FromScene(loaded)))
            .ShouldContain("{\"path\":\"Scripts/door.luau\"}");
    }

    [Fact]
    public void A_scene_carrying_a_script_demands_a_reader_that_can_keep_it()
    {
        // An older editor keeps 'script' as a member it does not know, and a
        // save from there drops it.
        var source = new Scene("Scripts");
        source.Root.CreateChild("Door").CreateChild("Opener").Script = new Script("Scripts/door.luau");

        MapDocument document = MapSceneBinder.FromScene(source);

        document.MinimumReadableVersion.ShouldBe(EngineInfo.ScriptMapVersion);
        EngineInfo.MapFormatVersion.ShouldBeGreaterThanOrEqualTo(EngineInfo.ScriptMapVersion);
        Should.NotThrow(() => MapReader.Read(MapWriter.Write(document)));
    }

    [Fact]
    public void A_scene_with_no_script_in_it_still_declares_the_oldest_reader()
    {
        var source = new Scene("Plain");
        source.Root.CreateChild("Group").CreateChild("Marker");

        // The root's own payload is never written.
        source.Root.Script = new Script("Scripts/never_saved.luau");

        MapDocument document = MapSceneBinder.FromScene(source);

        document.MinimumReadableVersion.ShouldBe(EngineInfo.MinimumReadableMapVersion);
        Encoding.UTF8.GetString(MapWriter.Write(document)).ShouldNotContain("\"script\"");
    }

    [Fact]
    public void A_document_built_in_code_whose_script_names_no_file_is_refused_naming_the_node()
    {
        // The reader checks a path it reads. This one never went through it.
        var document = new MapDocument();
        document.Nodes.Add(new MapNode { Id = Guid.NewGuid(), Name = "Opener", Script = new MapScript() });

        var thrown = Should.Throw<MapFormatException>(
            () => MapSceneBinder.ApplyTo(document, new Scene("S")));

        thrown.NodeName.ShouldBe("Opener");
    }
}
