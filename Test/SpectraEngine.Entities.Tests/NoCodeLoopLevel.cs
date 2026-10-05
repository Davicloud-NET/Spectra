using System.Text;

namespace SpectraEngine.Entities.Tests;

// The level the no-code loop tests play, as its .smap file reads. A corridor
// along +Z: the start faces down it, the trigger lies across it, and the door
// fills the gap between two walls at z = 4. Linked into the cook's tests
// too, which play the same level cooked.
internal static class NoCodeLoopLevel
{
    public const string Text = """
        {
          "spectramap": 4,
          "minimumReadableVersion": 4,
          "engine": "1.0.0",
          "scene": {
            "name": "NoCodeLoop"
          },
          "nodes": [
            {
              "id": "0d6f3b52-8c1e-4a79-b3f0-51c2a9e7d604",
              "name": "Floor",
              "transform": {"p":[0,-0.5,0]},
              "brush": {
                "planes": [
                  [1,0,0,-4],
                  [-1,0,0,-4],
                  [0,1,0,-0.5],
                  [0,-1,0,-0.5],
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
              "id": "3a91c7d0-2e54-4f6b-9d18-7b0e5c4a2f13",
              "name": "WallLeft",
              "transform": {"p":[-2.5,1.5,4]},
              "brush": {
                "planes": [
                  [1,0,0,-1.5],
                  [-1,0,0,-1.5],
                  [0,1,0,-1.5],
                  [0,-1,0,-1.5],
                  [0,0,1,-0.2],
                  [0,0,-1,-0.2]
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
              "id": "6e2b8f41-9a03-4c75-8d6e-1f4a7b9c0e28",
              "name": "WallRight",
              "transform": {"p":[2.5,1.5,4]},
              "brush": {
                "planes": [
                  [1,0,0,-1.5],
                  [-1,0,0,-1.5],
                  [0,1,0,-1.5],
                  [0,-1,0,-1.5],
                  [0,0,1,-0.2],
                  [0,0,-1,-0.2]
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
              "id": "91f4d2a6-5b7c-4e30-a8d9-3c6e0b1f7a45",
              "name": "Start",
              "transform": {"p":[0,0,-4]},
              "entity": {
                "class": "info_player_start"
              },
              "children": []
            },
            {
              "id": "b5c08e73-1d49-4a26-9f7b-8e2a6d3c0f51",
              "name": "Zone",
              "kind": "part",
              "collide": false,
              "query": false,
              "render": false,
              "transform": {"p":[0,1,0]},
              "brush": {
                "planes": [
                  [1,0,0,-2],
                  [-1,0,0,-2],
                  [0,1,0,-1],
                  [0,-1,0,-1],
                  [0,0,1,-1],
                  [0,0,-1,-1]
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
                "class": "trigger_once",
                "outputs": [
                  {"output":"OnTrigger","target":"Door","input":"Open"}
                ]
              },
              "children": []
            },
            {
              "id": "d7e3a109-6f28-4b5c-8a41-0c9d2e7b3f66",
              "name": "Door",
              "kind": "part",
              "transform": {"p":[0,1,4]},
              "brush": {
                "planes": [
                  [1,0,0,-1],
                  [-1,0,0,-1],
                  [0,1,0,-1],
                  [0,-1,0,-1],
                  [0,0,1,-0.2],
                  [0,0,-1,-0.2]
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
                "keys": {"lip":"0","speed":"4","wait":"-1"}
              },
              "children": []
            }
          ]
        }
        """;

    // The bytes a save writes: line feeds, and one at the end.
    public static byte[] Utf8() =>
        Encoding.UTF8.GetBytes(Text.ReplaceLineEndings("\n") + "\n");
}
