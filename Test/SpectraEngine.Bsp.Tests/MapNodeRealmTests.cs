using SpectraEngine.Core;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A node's declared realm and state: carried on the node, written by name,
/// and kept by a save. Nothing resolves them yet.
/// </summary>
public sealed class MapNodeRealmTests
{
    private static byte[] Utf8(string text) =>
        Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n") + "\n");

    // Hand-written. The unknown members pin where the two sit in a node.
    private const string Declared = """
        {
          "spectramap": 5,
          "minimumReadableVersion": 5,
          "engine": "1.0.0",
          "scene": {
            "name": "Realms"
          },
          "nodes": [
            {
              "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
              "name": "Vault",
              "afterName": 1,
              "realm": "server",
              "afterRealm": 2,
              "state": "dormant",
              "afterState": 3,
              "transform": {"p":[0,0,0]},
              "children": []
            }
          ]
        }
        """;

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
              "name": "Vault",
              "realm": "server",
              "transform": {"p":[0,0,0]},
              "children": [
                {
                  "id": "5b2f8a11-6c04-4e29-8d73-1af90b4e2c65",
                  "name": "EnemyTemplate",
                  "state": "dormant",
                  "transform": {"p":[0,0,0]},
                  "children": []
                }
              ]
            },
            {
              "id": "9c1e4d70-2a83-4f16-b5aa-0e6d3c8f21b7",
              "name": "Hud",
              "realm": "client",
              "state": "active",
              "transform": {"p":[0,0,0]},
              "children": []
            },
            {
              "id": "7d3c9b52-1e47-4f80-a6c1-2b5e8f904d13",
              "name": "Lobby",
              "realm": "shared",
              "transform": {"p":[0,0,0]},
              "children": []
            },
            {
              "id": "c4a81e06-93bd-4d52-8f17-6e0a2d7b5c39",
              "name": "Plain",
              "transform": {"p":[0,0,0]},
              "children": []
            }
          ]
        }
        """;

    [Fact]
    public void A_node_declares_nothing_until_told_to()
    {
        var node = new SceneNode("Plain");

        node.DeclaredRealm.ShouldBe(NodeRealm.Inherit);
        node.DeclaredState.ShouldBe(NodeState.Inherit);
    }

    [Fact]
    public void A_copy_declares_what_the_original_does()
    {
        var node = new SceneNode("Vault") { DeclaredRealm = NodeRealm.Server, DeclaredState = NodeState.Dormant };

        SceneNode copy = node.Clone();

        copy.DeclaredRealm.ShouldBe(NodeRealm.Server);
        copy.DeclaredState.ShouldBe(NodeState.Dormant);
    }

    [Fact]
    public void The_two_are_numbered_as_the_compiled_map_stores_them()
    {
        ((byte)NodeRealm.Inherit).ShouldBe((byte)ScmapNodeRealm.Inherit);
        ((byte)NodeRealm.Shared).ShouldBe((byte)ScmapNodeRealm.Shared);
        ((byte)NodeRealm.Server).ShouldBe((byte)ScmapNodeRealm.Server);
        ((byte)NodeRealm.Client).ShouldBe((byte)ScmapNodeRealm.Client);

        ((byte)NodeState.Inherit).ShouldBe((byte)ScmapNodeState.Inherit);
        ((byte)NodeState.Active).ShouldBe((byte)ScmapNodeState.Active);
        ((byte)NodeState.Dormant).ShouldBe((byte)ScmapNodeState.Dormant);
    }

    [Fact]
    public void Realm_and_state_round_trip_byte_for_byte_with_unknown_members_between_them()
    {
        byte[] source = Utf8(Declared);

        MapDocument document = MapReader.Read(source);

        document.Nodes[0].Realm.ShouldBe(NodeRealm.Server);
        document.Nodes[0].State.ShouldBe(NodeState.Dormant);
        document.Nodes[0].Unknown.Count.ShouldBe(3, "the two are decoded, never carried as unknown members");
        MapWriter.Write(document).ShouldBe(source);
    }

    [Fact]
    public void A_map_that_declares_realms_and_states_saves_byte_for_byte_after_a_load()
    {
        byte[] source = Utf8(Level);
        var loaded = new Scene("Testmap");

        MapSceneBinder.ApplyTo(MapReader.Read(source), loaded);

        SceneNode vault = loaded.Root.Children[0];
        vault.DeclaredRealm.ShouldBe(NodeRealm.Server);
        vault.DeclaredState.ShouldBe(NodeState.Inherit);
        vault.Children[0].DeclaredRealm.ShouldBe(
            NodeRealm.Inherit, "the file states the node's own, never its parent's");
        vault.Children[0].DeclaredState.ShouldBe(NodeState.Dormant);
        loaded.Root.Children[1].DeclaredRealm.ShouldBe(NodeRealm.Client);
        loaded.Root.Children[1].DeclaredState.ShouldBe(NodeState.Active);
        loaded.Root.Children[2].DeclaredRealm.ShouldBe(NodeRealm.Shared);
        loaded.Root.Children[3].DeclaredRealm.ShouldBe(NodeRealm.Inherit);

        MapWriter.Write(MapSceneBinder.FromScene(loaded)).ShouldBe(source);
    }

    [Theory]
    [InlineData(NodeRealm.Shared, "shared")]
    [InlineData(NodeRealm.Server, "server")]
    [InlineData(NodeRealm.Client, "client")]
    public void Each_realm_survives_a_scene_round_trip_by_its_name(NodeRealm realm, string name)
    {
        var source = new Scene("Realms");
        source.Root.CreateChild("Node").DeclaredRealm = realm;

        byte[] bytes = MapWriter.Write(MapSceneBinder.FromScene(source));
        var loaded = new Scene("Realms");
        MapSceneBinder.ApplyTo(MapReader.Read(bytes), loaded);

        loaded.Root.Children[0].DeclaredRealm.ShouldBe(realm);
        loaded.Root.Children[0].DeclaredState.ShouldBe(NodeState.Inherit);
        Encoding.UTF8.GetString(bytes).ShouldContain($"\"realm\": \"{name}\"");
    }

    [Theory]
    [InlineData(NodeState.Active, "active")]
    [InlineData(NodeState.Dormant, "dormant")]
    public void Each_state_survives_a_scene_round_trip_by_its_name(NodeState state, string name)
    {
        var source = new Scene("States");
        source.Root.CreateChild("Node").DeclaredState = state;

        byte[] bytes = MapWriter.Write(MapSceneBinder.FromScene(source));
        var loaded = new Scene("States");
        MapSceneBinder.ApplyTo(MapReader.Read(bytes), loaded);

        loaded.Root.Children[0].DeclaredState.ShouldBe(state);
        loaded.Root.Children[0].DeclaredRealm.ShouldBe(NodeRealm.Inherit);
        Encoding.UTF8.GetString(bytes).ShouldContain($"\"state\": \"{name}\"");
    }

    [Fact]
    public void A_node_that_declares_nothing_writes_neither_and_keeps_the_oldest_reader()
    {
        var source = new Scene("Plain");
        source.Root.CreateChild("Group").CreateChild("Marker");

        MapDocument document = MapSceneBinder.FromScene(source);
        string text = Encoding.UTF8.GetString(MapWriter.Write(document));

        text.ShouldNotContain("\"realm\"");
        text.ShouldNotContain("\"state\"");
        document.MinimumReadableVersion.ShouldBe(EngineInfo.MinimumReadableMapVersion);
    }

    [Theory]
    [InlineData(NodeRealm.Server, NodeState.Inherit)]
    [InlineData(NodeRealm.Inherit, NodeState.Dormant)]
    public void A_scene_that_declares_a_realm_or_a_state_demands_a_reader_that_keeps_it(
        NodeRealm realm, NodeState state)
    {
        // An older editor reads both and its save drops them. A server node
        // would come back shared.
        var source = new Scene("Realms");
        SceneNode nested = source.Root.CreateChild("Group").CreateChild("Vault");
        nested.DeclaredRealm = realm;
        nested.DeclaredState = state;

        MapSceneBinder.FromScene(source).MinimumReadableVersion.ShouldBe(EngineInfo.NodeRealmMapVersion);
    }

    [Theory]
    [InlineData("realm", "sever")]
    [InlineData("realm", "inherit")]
    [InlineData("realm", "Server")]
    [InlineData("state", "asleep")]
    [InlineData("state", "inherit")]
    public void A_realm_or_state_outside_its_vocabulary_is_refused_by_name(string member, string value)
    {
        // A typo that fell through to shared would hand a server node to every client.
        byte[] document = Utf8($$"""
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
                  "{{member}}": "{{value}}",
                  "transform": {"p":[0,0,0]},
                  "children": []
                }
              ]
            }
            """);

        var thrown = Should.Throw<MapFormatException>(() => MapReader.Read(document));

        thrown.NodeName.ShouldBe("Suspect");
        thrown.ByteOffset.ShouldBeGreaterThan(0);
        thrown.Message.ShouldContain(member);
        thrown.Message.ShouldContain(value);
    }
}
