using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A scene saved to a map and loaded back is the same scene, and a bundle on
/// disk behaves like a folder someone else also edits.
/// </summary>
public sealed class MapSceneRoundTripTests
{
    private static Scene NewScene()
    {
        var scene = new Scene("Testmap");
        return scene;
    }

    private static Brush Box(float half = 1f, MaterialRef material = default) =>
        Brush.CreateBox(new Vector3(-half), new Vector3(half), material);

    [Fact]
    public void A_saved_scene_comes_back_with_the_same_identities_and_order()
    {
        Scene source = NewScene();
        for (int i = 0; i < 4; i++)
        {
            SceneNode node = source.Root.CreateChild($"Brush{i}");
            node.LocalPosition = new Vector3(i * 3f, 0f, 0f);
            node.Brush = Box();
        }
        SceneNode nested = source.Root.Children[1].CreateChild("Child");
        nested.LocalPosition = new Vector3(0f, 5f, 0f);

        Guid[] expectedIds = new Guid[4];
        for (int i = 0; i < 4; i++) expectedIds[i] = source.Root.Children[i].Id;
        Guid nestedId = nested.Id;

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), loaded);

        loaded.Name.ShouldBe("Testmap");
        loaded.Root.Children.Count.ShouldBe(4);
        for (int i = 0; i < 4; i++)
        {
            loaded.Root.Children[i].Id.ShouldBe(expectedIds[i]);
            loaded.Root.Children[i].Name.ShouldBe($"Brush{i}");
        }

        loaded.Root.Children[1].Children.Count.ShouldBe(1);
        loaded.Root.Children[1].Children[0].Id.ShouldBe(nestedId);
    }

    [Fact]
    public void A_loaded_node_is_findable_by_its_id()
    {
        // A load attaches each subtree with one AddChild; that one event has
        // to index the descendants too.
        Scene source = NewScene();
        SceneNode parent = source.Root.CreateChild("Parent");
        SceneNode child = parent.CreateChild("Child");
        SceneNode grandchild = child.CreateChild("Grandchild");
        Guid parentId = parent.Id, childId = child.Id, grandchildId = grandchild.Id;

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), loaded);

        loaded.TryFindById(parentId, out SceneNode? foundParent).ShouldBeTrue("root child");
        foundParent!.Name.ShouldBe("Parent");
        loaded.TryFindById(childId, out SceneNode? foundChild).ShouldBeTrue("one level down");
        foundChild!.Name.ShouldBe("Child");
        loaded.TryFindById(grandchildId, out SceneNode? foundGrandchild).ShouldBeTrue("two levels down");
        foundGrandchild!.Name.ShouldBe("Grandchild");
    }

    [Fact]
    public void Sibling_order_survives_a_round_trip()
    {
        // Child order is placement order, which breaks ties in the carve.
        Scene source = NewScene();
        string[] names = ["Floor", "Wall", "Doorway", "Ceiling", "Trim"];
        foreach (string name in names)
            source.Root.CreateChild(name).Brush = Box();

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), loaded);

        for (int i = 0; i < names.Length; i++)
            loaded.Root.Children[i].Name.ShouldBe(names[i]);
    }

    [Fact]
    public void Every_loaded_brush_is_its_own_instance()
    {
        // The compile caches key on Brush reference identity; a shared
        // instance re-carves on every compile.
        Scene source = NewScene();
        Brush shared = Box();
        source.Root.CreateChild("A").Brush = shared;
        source.Root.CreateChild("B").Brush = shared;

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), loaded);

        loaded.Root.Children[0].Brush.ShouldNotBeSameAs(loaded.Root.Children[1].Brush);
    }

    [Fact]
    public void Both_declared_bits_survive_independently()
    {
        Scene source = NewScene();
        (string Name, BrushKind Kind, BrushOperation Operation)[] cases =
        [
            ("WorldAdd", BrushKind.World, BrushOperation.Additive),
            ("WorldCut", BrushKind.World, BrushOperation.Subtractive),
            ("PartAdd", BrushKind.Part, BrushOperation.Additive),
            ("PartCut", BrushKind.Part, BrushOperation.Subtractive),
        ];

        foreach ((string name, BrushKind kind, BrushOperation operation) in cases)
        {
            SceneNode node = source.Root.CreateChild(name);
            node.BrushKind = kind;
            node.Brush = Box().WithOperation(operation);
        }

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), loaded);

        for (int i = 0; i < cases.Length; i++)
        {
            loaded.Root.Children[i].BrushKind.ShouldBe(cases[i].Kind, cases[i].Name);
            loaded.Root.Children[i].Brush!.Operation.ShouldBe(cases[i].Operation, cases[i].Name);
        }
    }

    [Fact]
    public void A_face_material_round_trips_as_a_path_rather_than_an_id()
    {
        // MaterialRef ids are per-process intern order, so they cannot be written.
        MaterialRef material = MaterialRegistry.Intern("Materials/roundtrip_probe.spectramat");

        Scene source = NewScene();
        source.Root.CreateChild("Wall").Brush = Box(1f, material);

        byte[] bytes = MapWriter.Write(MapSceneBinder.FromScene(source));
        Encoding.UTF8.GetString(bytes).ShouldContain("Materials/roundtrip_probe.spectramat");

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(bytes), loaded);

        loaded.Root.Children[0].Brush!.FaceSurfaces[0].Material.ShouldBe(material);
    }

    [Fact]
    public void An_explicit_texture_axis_survives_and_a_world_aligned_face_stays_absent()
    {
        Scene source = NewScene();
        Brush brush = Box().WithFaceSurface(2, new FaceSurface(
            MaterialRef.Default,
            new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f),
            uOffset: 0.25f, vOffset: -0.5f, uScale: 2f, vScale: 4f));
        source.Root.CreateChild("Wall").Brush = brush;

        byte[] bytes = MapWriter.Write(MapSceneBinder.FromScene(source));
        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(bytes), loaded);

        FaceSurface[] faces = [.. loaded.Root.Children[0].Brush!.FaceSurfaces];
        faces[2].UAxis.ShouldBe(new Vector3(0f, 0f, 1f));
        faces[2].VAxis.ShouldBe(new Vector3(0f, 1f, 0f));
        faces[2].UOffset.ShouldBe(0.25f);
        faces[2].VOffset.ShouldBe(-0.5f);
        faces[2].UScale.ShouldBe(2f);
        faces[2].VScale.ShouldBe(4f);

        faces[0].IsWorldAligned.ShouldBeTrue();
        Encoding.UTF8.GetString(bytes).ShouldNotContain("\"u\":[0,0,0]");
    }

    [Fact]
    public void A_light_survives_with_every_field()
    {
        Scene source = NewScene();
        SceneNode sun = source.Root.CreateChild("Sun");
        sun.Light = new Light
        {
            Kind = LightKind.Point,
            Color = new Vector3(0.2f, 0.4f, 0.8f),
            Intensity = 7.5f,
            Range = 33f,
            Enabled = false,
        };

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), loaded);

        Light light = loaded.Root.Children[0].Light.ShouldNotBeNull();
        light.Kind.ShouldBe(LightKind.Point);
        light.Color.ShouldBe(new Vector3(0.2f, 0.4f, 0.8f));
        light.Intensity.ShouldBe(7.5f);
        light.Range.ShouldBe(33f);
        light.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void A_default_light_round_trips_without_tripping_the_range_guard()
    {
        // Light.Range throws on 0, so an omitted range must not read back as 0.
        Scene source = NewScene();
        source.Root.CreateChild("Sun").Light = new Light();

        Scene loaded = NewScene();
        Should.NotThrow(() =>
            MapSceneBinder.ApplyTo(MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), loaded));

        loaded.Root.Children[0].Light!.Range.ShouldBe(10f);
    }

    [Fact]
    public void An_entity_payload_survives_the_projection_to_a_document()
    {
        // FromScene builds a fresh MapNode, so the entity has to be bound,
        // not carried as an unknown.
        Scene source = NewScene();
        SceneNode door = source.Root.CreateChild("Door");
        var entity = new EntityData("func_door");
        entity.SetValue("speed", "100");
        entity.Connections.Add(new EntityConnection("OnFullyOpen", "light1", "TurnOn", "", 0f, -1));
        door.Entity = entity;

        MapDocument document = MapSceneBinder.FromScene(source);

        MapEntity mapped = document.Nodes[0].Entity.ShouldNotBeNull();
        mapped.Class.ShouldBe("func_door");
        mapped.Keys.ShouldBe([new KeyValuePair<string, string>("speed", "100")]);
        mapped.Outputs.Count.ShouldBe(1);
        mapped.Outputs[0].Output.ShouldBe("OnFullyOpen");
        mapped.Outputs[0].Target.ShouldBe("light1");
        mapped.Outputs[0].Input.ShouldBe("TurnOn");

        Encoding.UTF8.GetString(MapWriter.Write(document)).ShouldContain("func_door");
    }

    [Fact]
    public void An_entity_survives_a_scene_round_trip_with_every_field()
    {
        Scene source = NewScene();
        source.Root.CreateChild("light1");
        SceneNode door = source.Root.CreateChild("Door");
        var entity = new EntityData("func_door");
        entity.SetValue("speed", "100");
        entity.SetValue("message", "it opens");
        entity.Connections.Add(new EntityConnection("OnFullyOpen", "light1", "TurnOn", "3", 1.5f, 2));
        door.Entity = entity;

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), loaded);

        EntityData back = loaded.Root.Children[1].Entity.ShouldNotBeNull();
        back.ClassName.ShouldBe("func_door");
        back.Keyvalues.ShouldBe([
            new KeyValuePair<string, string>("speed", "100"),
            new KeyValuePair<string, string>("message", "it opens"),
        ], "keyvalue order is the file's order, and nothing may sort it");
        back.Connections.ShouldBe([new EntityConnection("OnFullyOpen", "light1", "TurnOn", "3", 1.5f, 2)]);
    }

    [Fact]
    public void An_entity_class_this_build_has_never_heard_of_still_loads()
    {
        Scene source = NewScene();
        source.Root.CreateChild("Mystery").Entity = new EntityData("xyzzy_unknown");

        Scene loaded = NewScene();
        Should.NotThrow(() =>
            MapSceneBinder.ApplyTo(MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), loaded));

        loaded.Root.Children[0].Entity!.ClassName.ShouldBe("xyzzy_unknown");
    }

    [Fact]
    public void A_map_for_a_game_this_build_does_not_have_comes_back_byte_for_byte()
    {
        // No geometry in the fixture: Brush re-normalises planes, which would
        // break byte identity through a scene. A save stamps the engine's
        // format version, so 'spectramap' is the current one.
        byte[] source = Encoding.UTF8.GetBytes("""
            {
              "spectramap": 4,
              "minimumReadableVersion": 3,
              "engine": "1.0.0",
              "scene": {
                "name": "Wired"
              },
              "nodes": [
                {
                  "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
                  "name": "Mystery",
                  "transform": {"p":[0,0,0]},
                  "entity": {
                    "class": "xyzzy_unknown",
                    "keys": {"colour":"green","plugh":"1"},
                    "outputs": [
                      {"output":"OnPlugh","target":"Mystery","input":"Xyzzy","param":"y2","delay":0.5,"times":3}
                    ]
                  },
                  "children": []
                }
              ]
            }
            """.ReplaceLineEndings("\n") + "\n");

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(source), loaded);

        MapWriter.Write(MapSceneBinder.FromScene(loaded)).ShouldBe(source);
    }

    [Fact]
    public void A_duplicated_keyvalue_is_kept_rather_than_merged()
    {
        // A binder using SetValue instead of Add would collapse the pair.
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {
              "spectramap": 3,
              "minimumReadableVersion": 3,
              "engine": "1.0.0",
              "scene": {
                "name": "S"
              },
              "nodes": [
                {
                  "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
                  "name": "Twice",
                  "transform": {"p":[0,0,0]},
                  "entity": {
                    "class": "func_door",
                    "keys": {"speed":"100","speed":"200"}
                  },
                  "children": []
                }
              ]
            }
            """.ReplaceLineEndings("\n") + "\n");

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapReader.Read(bytes), loaded);

        EntityData entity = loaded.Root.Children[0].Entity.ShouldNotBeNull();
        entity.Keyvalues.Count.ShouldBe(2);
        entity.TryGetValue("speed", out string first).ShouldBeTrue();
        first.ShouldBe("100", "the first match wins, which is what a preserved duplicate needs");
    }

    [Fact]
    public void A_map_with_no_entity_in_it_still_declares_the_oldest_reader()
    {
        Scene source = NewScene();
        source.Root.CreateChild("Wall").Brush = Box();

        MapSceneBinder.FromScene(source).MinimumReadableVersion
            .ShouldBe(EngineInfo.MinimumReadableMapVersion);
    }

    [Fact]
    public void A_map_carrying_an_entity_demands_a_reader_that_can_keep_it()
    {
        // An older editor would drop the entity on save.
        Scene source = NewScene();
        source.Root.CreateChild("Door").Entity = new EntityData("func_door");

        MapSceneBinder.FromScene(source).MinimumReadableVersion
            .ShouldBe(EngineInfo.EntityMapVersion);
    }

    [Fact]
    public void A_map_carrying_both_an_entity_and_a_light_shape_demands_the_newer_reader()
    {
        Scene source = NewScene();
        source.Root.CreateChild("Panel").Light = new Light { Kind = LightKind.Rect };
        source.Root.CreateChild("Door").Entity = new EntityData("func_door");

        MapSceneBinder.FromScene(source).MinimumReadableVersion.ShouldBe(
            Math.Max(EngineInfo.EntityMapVersion, EngineInfo.LightShapeMapVersion));
    }

    [Fact]
    public void A_wire_naming_a_target_this_map_does_not_have_is_kept_and_warned_about()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {
              "spectramap": 3,
              "minimumReadableVersion": 3,
              "engine": "1.0.0",
              "scene": {
                "name": "S"
              },
              "nodes": [
                {
                  "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
                  "name": "Button",
                  "transform": {"p":[0,0,0]},
                  "entity": {
                    "class": "func_button",
                    "outputs": [
                      {"output":"OnPressed","target":"door_that_was_renamed","input":"Open"}
                    ]
                  },
                  "children": []
                }
              ]
            }
            """.ReplaceLineEndings("\n") + "\n");

        Scene loaded = NewScene();
        var report = new MapLoadReport();
        MapDocument document = MapReader.Read(bytes);
        MapSceneBinder.ApplyTo(document, loaded, report);

        report.IsComplete.ShouldBeFalse();
        report.UnresolvedTargets.Count.ShouldBe(1);
        report.UnresolvedTargets[0].ShouldContain("door_that_was_renamed");
        report.UnresolvedTargets[0].ShouldContain("OnPressed");
        report.Describe().ShouldNotBeNull();

        loaded.Root.Children[0].Entity!.Connections[0].TargetName.ShouldBe("door_that_was_renamed");
        MapWriter.Write(document).ShouldBe(bytes,
            "a warning must not change what is written, or the next save would delete the wiring");
    }

    [Theory]
    // Runtime tokens, resolved when the output fires.
    [InlineData("!self", true)]
    [InlineData("!activator", true)]
    [InlineData("!caller", true)]
    // Trailing star: prefix match.
    [InlineData("door_*", true)]
    [InlineData("gate_*", false)]
    // An empty target names nothing, which is fine on a half-wired entity.
    [InlineData("", true)]
    [InlineData("door_left", true)]
    [InlineData("Door_Left", false)]
    [InlineData("door_middle", false)]
    public void A_target_resolves_by_name_by_runtime_form_or_by_prefix(string target, bool resolves)
    {
        Scene source = NewScene();
        source.Root.CreateChild("door_left");
        SceneNode button = source.Root.CreateChild("Button");
        var entity = new EntityData("func_button");
        entity.Connections.Add(new EntityConnection("OnPressed", target, "Open", "", 0f, -1));
        button.Entity = entity;

        var report = new MapLoadReport();
        MapSceneBinder.ApplyTo(
            MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(source))), NewScene(), report);

        report.UnresolvedTargets.Count.ShouldBe(resolves ? 0 : 1, $"target '{target}'");
    }

    [Fact]
    public void A_mesh_built_in_code_is_reported_rather_than_dropped_in_silence()
    {
        // A mesh built from raw arrays has no file a map could name.
        Scene source = NewScene();
        source.Root.CreateChild("Procedural").MeshRenderer =
            new MeshRenderer(new FakeMesh(), new Material(null));
        source.Root.CreateChild("Wall").Brush = Box();

        var report = new MapSaveReport();
        MapDocument document = MapSceneBinder.FromScene(source, report);

        report.IsComplete.ShouldBeFalse();
        report.UnsourcedMeshNodes.ShouldBe(["Procedural"]);
        report.Describe().ShouldNotBeNull();

        // Only the geometry is lost; the node is still written.
        document.Nodes.Count.ShouldBe(2);
        document.Nodes[0].Name.ShouldBe("Procedural");
        document.Nodes[0].Mesh.ShouldBeNull();
    }

    [Fact]
    public void A_mesh_from_a_model_is_written_as_a_reference()
    {
        Scene source = NewScene();
        SceneNode prop = source.Root.CreateChild("Crate");
        prop.MeshRenderer = new MeshRenderer(new FakeMesh(), new Material(null));
        prop.MeshSource = new MeshSource("Models/crate.obj", 2);

        var report = new MapSaveReport();
        byte[] bytes = MapWriter.Write(MapSceneBinder.FromScene(source, report));

        report.IsComplete.ShouldBeTrue("a node that names a model is fully writable");
        string text = Encoding.UTF8.GetString(bytes);
        text.ShouldContain("""{"model":"Models/crate.obj","submesh":2}""");

        MapDocument reread = MapReader.Read(bytes);
        reread.Nodes[0].Mesh.ShouldNotBeNull();
        reread.Nodes[0].Mesh!.Model.ShouldBe("Models/crate.obj");
        reread.Nodes[0].Mesh.Submesh.ShouldBe(2);
    }

    [Fact]
    public void A_single_submesh_prop_omits_the_index()
    {
        Scene source = NewScene();
        SceneNode prop = source.Root.CreateChild("Crate");
        prop.MeshRenderer = new MeshRenderer(new FakeMesh(), new Material(null));
        prop.MeshSource = new MeshSource("Models/crate.obj", 0);

        Encoding.UTF8.GetString(MapWriter.Write(MapSceneBinder.FromScene(source)))
            .ShouldContain("""{"model":"Models/crate.obj"}""");
    }

    [Fact]
    public void Detaching_the_renderer_clears_the_source_it_described()
    {
        // Otherwise a save would name a model the node no longer draws.
        var node = new SceneNode("Prop")
        {
            MeshRenderer = new MeshRenderer(new FakeMesh(), new Material(null)),
        };
        node.MeshSource = new MeshSource("Models/crate.obj", 0);

        node.MeshRenderer = null;

        node.MeshSource.ShouldBeNull();
    }

    [Fact]
    public void A_map_naming_a_model_the_project_does_not_have_still_loads()
    {
        // Unlike a bad brush, which throws: a missing prop is not a hole in the world.
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {
              "spectramap": 1,
              "minimumReadableVersion": 1,
              "engine": "1.0.0",
              "scene": {
                "name": "S"
              },
              "nodes": [
                {
                  "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
                  "name": "GhostProp",
                  "transform": {"p":[1,2,3]},
                  "mesh": {"model":"Models/does_not_exist.obj"},
                  "children": []
                }
              ]
            }
            """);

        Scene loaded = NewScene();
        var report = new MapLoadReport();
        Should.NotThrow(() => MapSceneBinder.ApplyTo(MapReader.Read(bytes), loaded, report));

        loaded.Root.Children.Count.ShouldBe(1);
        loaded.Root.Children[0].Name.ShouldBe("GhostProp");
        loaded.Root.Children[0].LocalPosition.ShouldBe(new Vector3(1f, 2f, 3f));
        loaded.Root.Children[0].MeshRenderer.ShouldBeNull();

        report.IsComplete.ShouldBeFalse();
        report.UnresolvedMeshes.Count.ShouldBe(1);
        report.UnresolvedMeshes[0].ShouldContain("GhostProp");
        report.UnresolvedMeshes[0].ShouldContain("Models/does_not_exist.obj");
    }

    [Fact]
    public void A_mesh_record_with_no_model_is_refused()
    {
        var thrown = Should.Throw<MapFormatException>(() => MapReader.Read(Encoding.UTF8.GetBytes("""
            {
              "spectramap": 1,
              "minimumReadableVersion": 1,
              "engine": "1.0.0",
              "scene": {
                "name": "S"
              },
              "nodes": [
                {
                  "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
                  "name": "Nameless",
                  "transform": {"p":[0,0,0]},
                  "mesh": {"submesh":1},
                  "children": []
                }
              ]
            }
            """)));

        thrown.NodeName.ShouldBe("Nameless");
        thrown.Message.ShouldContain("model");
    }

    [Fact]
    public void A_scene_with_nothing_unwritable_reports_complete()
    {
        Scene source = NewScene();
        source.Root.CreateChild("Wall").Brush = Box();

        var report = new MapSaveReport();
        MapSceneBinder.FromScene(source, report);

        report.IsComplete.ShouldBeTrue();
        report.Describe().ShouldBeNull();
    }

    [Fact]
    public void A_hand_edited_plane_set_that_cannot_build_names_the_node_and_the_offset()
    {
        // Valid JSON, so the reader accepts it; Brush's constructor rejects it
        // a stage later.
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {
              "spectramap": 1,
              "minimumReadableVersion": 1,
              "engine": "1.0.0",
              "scene": {
                "name": "S"
              },
              "nodes": [
                {
                  "id": "3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4",
                  "name": "Impossible",
                  "transform": {"p":[0,0,0]},
                  "brush": {
                    "planes": [
                      [1,0,0,-1],
                      [1,0,0,-1]
                    ],
                    "faces": [{},{}]
                  },
                  "children": []
                }
              ]
            }
            """);

        MapDocument document = MapReader.Read(bytes);
        var thrown = Should.Throw<MapFormatException>(() => MapSceneBinder.ApplyTo(document, NewScene()));

        thrown.NodeName.ShouldBe("Impossible");
        thrown.ByteOffset.ShouldBeGreaterThan(0, "the offset must survive into the scene-building stage");
    }

    [Fact]
    public void A_save_with_no_edits_in_it_does_not_touch_the_file()
    {
        using var bundle = new TemporaryBundle();
        Scene source = NewScene();
        source.Root.CreateChild("Wall").Brush = Box();

        MapBundle.Save(bundle.Path, MapSceneBinder.FromScene(source)).ShouldBeTrue();
        DateTime firstWrite = File.GetLastWriteTimeUtc(MapBundle.DocumentPath(bundle.Path));

        MapBundle.Save(bundle.Path, MapBundle.Load(bundle.Path)).ShouldBeFalse(
            "an unedited document must reproduce its own bytes, so there is nothing to write");
        File.GetLastWriteTimeUtc(MapBundle.DocumentPath(bundle.Path)).ShouldBe(firstWrite);
    }

    [Fact]
    public void A_save_leaves_every_file_it_does_not_reference_alone()
    {
        using var bundle = new TemporaryBundle();
        Scene source = NewScene();
        source.Root.CreateChild("Wall").Brush = Box();
        MapBundle.Save(bundle.Path, MapSceneBinder.FromScene(source));

        string notes = Path.Combine(bundle.Path, "NOTES.md");
        File.WriteAllText(notes, "do not delete me");
        Directory.CreateDirectory(Path.Combine(bundle.Path, "reference"));

        source.Root.CreateChild("Second").Brush = Box();
        MapBundle.Save(bundle.Path, MapSceneBinder.FromScene(source)).ShouldBeTrue();

        File.ReadAllText(notes).ShouldBe("do not delete me");
        Directory.Exists(Path.Combine(bundle.Path, "reference")).ShouldBeTrue();
    }

    [Fact]
    public void A_bundle_round_trips_through_disk()
    {
        using var bundle = new TemporaryBundle();
        Scene source = NewScene();
        SceneNode wall = source.Root.CreateChild("Wall");
        wall.Brush = Box();
        wall.LocalPosition = new Vector3(2f, 3f, 4f);

        MapBundle.Save(bundle.Path, MapSceneBinder.FromScene(source));
        MapBundle.IsBundle(bundle.Path).ShouldBeTrue();

        Scene loaded = NewScene();
        MapSceneBinder.ApplyTo(MapBundle.Load(bundle.Path), loaded);

        loaded.Root.Children[0].Name.ShouldBe("Wall");
        loaded.Root.Children[0].LocalPosition.ShouldBe(new Vector3(2f, 3f, 4f));
        loaded.Root.Children[0].Brush.ShouldNotBeNull();
    }

    [Fact]
    public void A_directory_with_no_document_in_it_is_not_a_bundle()
    {
        using var bundle = new TemporaryBundle();

        MapBundle.IsBundle(bundle.Path).ShouldBeFalse();
        Should.Throw<FileNotFoundException>(() => MapBundle.Load(bundle.Path));
    }

    private sealed class TemporaryBundle : IDisposable
    {
        public TemporaryBundle()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"spectra_map_{Guid.NewGuid():N}{MapFormat.BundleExtension}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* leftover temp dir is fine */ }
        }
    }

    private sealed class FakeMesh : Mesh
    {
        public override void Draw() { }
        public override void DrawInstanced(InstanceBuffer instances, int count, int firstInstance = 0) { }
        public override void Dispose() { }
    }
}
