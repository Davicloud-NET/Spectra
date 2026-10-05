using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using System;
using System.Text;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A door loaded from a map: it runs from what the file says, and the file
/// saves the same once the level stops.
/// </summary>
public sealed class FuncDoorMapTests
{
    // A floor in the static world, a door that is a part, and a logic_auto
    // that opens the door when the level starts.
    private const string DoorFixture = """
        {
          "spectramap": 4,
          "minimumReadableVersion": 3,
          "engine": "1.0.0",
          "scene": {
            "name": "DoorFixture"
          },
          "nodes": [
            {
              "id": "5f0c1c3e-7a44-4c0b-9a57-2d1e8b6f4a10",
              "name": "Floor",
              "transform": {"p":[0,-1,0]},
              "brush": {
                "planes": [
                  [1,0,0,-8],
                  [-1,0,0,-8],
                  [0,1,0,-1],
                  [0,-1,0,-1],
                  [0,0,1,-8],
                  [0,0,-1,-8]
                ],
                "faces": [
                  {},
                  {},
                  {},
                  {},
                  {},
                  {}
                ]
              },
              "children": []
            },
            {
              "id": "a2b7d9e4-13f6-4d58-8c02-7e5a9b0c6d31",
              "name": "Door",
              "kind": "part",
              "transform": {"p":[2,1,-3]},
              "brush": {
                "planes": [
                  [1,0,0,-0.5],
                  [-1,0,0,-0.5],
                  [0,1,0,-1],
                  [0,-1,0,-1],
                  [0,0,1,-0.1],
                  [0,0,-1,-0.1]
                ],
                "faces": [
                  {},
                  {},
                  {},
                  {},
                  {},
                  {}
                ]
              },
              "entity": {
                "class": "func_door",
                "keys": {"speed":"3","wait":"0.5"}
              },
              "children": []
            },
            {
              "id": "c93e5a17-0b6d-4f82-b1a4-6f2d7c8e9a05",
              "name": "Starter",
              "transform": {"p":[0,0,0]},
              "entity": {
                "class": "logic_auto",
                "outputs": [
                  {"output":"OnMapSpawn","target":"Door","input":"Open"}
                ]
              },
              "children": []
            }
          ]
        }
        """;

    [Fact]
    public void The_map_saves_the_same_bytes_after_Stop()
    {
        byte[] source = Utf8(DoorFixture);
        var scene = new Scene("Empty");
        MapSceneBinder.ApplyTo(MapReader.Read(source), scene);
        Same(source, MapWriter.Write(MapSceneBinder.FromScene(scene)));

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        // 1.95 at 3 a second is 39 ticks, and the logic_auto opens it on the first.
        Movers.Run(world, 39);
        SceneNode door = scene.Root.Children.Single(node => node.Name == "Door");
        EntityRuntime.Live<FuncDoor>(world, door).IsFullyOpen.ShouldBeTrue();
        door.LocalPosition.Y.ShouldBe(2.95f, 1e-5f);
        MapWriter.Write(MapSceneBinder.FromScene(scene)).ShouldNotBe(source, "the door is open now");

        // Stopped part way through closing.
        Movers.Run(world, 45);
        world.Deactivate();

        Same(source, MapWriter.Write(MapSceneBinder.FromScene(scene)));
    }

    private static byte[] Utf8(string text) =>
        Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n") + "\n");

    private static void Same(byte[] expected, byte[] actual)
    {
        if (expected.AsSpan().SequenceEqual(actual))
            return;

        throw new Xunit.Sdk.XunitException(
            "The document changed on the way through.\n"
            + $"--- expected ---\n{Encoding.UTF8.GetString(expected)}\n"
            + $"--- actual ---\n{Encoding.UTF8.GetString(actual)}");
    }
}
