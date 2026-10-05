using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>Putting a sound file into the scene through the editor host.</summary>
public sealed class SoundInsertTests
{
    private const string Speaker = "test_speaker";
    private const string DoorOpen = "Sounds/door_open.wav";

    // The classes exist only as .sentdef bytes, the way the editor learns any class.
    private static Scene SceneWith(params EntitySchema[] schemas) => new("sounds")
    {
        EntitySchemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(schemas)),
    };

    private static Scene SceneWithASpeaker() => SceneWith(
        new EntitySchema("test_relay", placement: EntityPlacement.Abstract),
        new EntitySchema(Speaker, keyvalues: [Setting("volume", KeyvalueType.Float), Setting("file", KeyvalueType.AssetSound)]));

    private static KeyvalueDescriptor Setting(string name, KeyvalueType type) =>
        new(name, "", "", "", type, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u, KeyvalueDescriptor.NoChoices);

    private static SceneEditorHost NewHost(Scene scene)
    {
        var renderer = new CompilingRenderer();

        // The host reads the viewport size in its constructor; zero breaks picking.
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));

        return new SceneEditorHost(
            NullLoggerFactory.Instance, scene, renderer, new InputManager(NullLogger<InputManager>.Instance));
    }

    [Fact]
    public void A_dropped_sound_becomes_an_entity_that_plays_the_file_and_is_named_after_it()
    {
        Scene scene = SceneWithASpeaker();
        SceneEditorHost host = NewHost(scene);

        SoundInsertReport report = host.InsertSound(DoorOpen);

        report.Placed.ShouldBeTrue(report.Describe());
        report.Refused.ShouldBeNull();
        report.NodeName.ShouldBe("door_open");
        report.Describe().ShouldBe("Sounds/door_open.wav placed as the sound 'door_open'.");

        SceneNode node = scene.Root.Children.ShouldHaveSingleItem();
        node.Id.ShouldBe(report.NodeId);
        node.Name.ShouldBe("door_open");
        node.Brush.ShouldBeNull();

        EntityData entity = node.Entity.ShouldNotBeNull();
        entity.ClassName.ShouldBe(Speaker);
        entity.Keyvalues.ShouldHaveSingleItem().ShouldBe(new("file", DoorOpen));
    }

    [Fact]
    public void A_dropped_sound_is_one_history_entry_and_is_selected()
    {
        Scene scene = SceneWithASpeaker();
        SceneEditorHost host = NewHost(scene);

        host.InsertSound(DoorOpen);

        SceneNode node = scene.Root.Children[0];
        scene.Selection.Items.ShouldBe([node]);
        host.UndoDepth.ShouldBe(1, "the entity and its setting are one thing the user did");

        Guid id = node.Id;
        host.Apply(EditorHostCommand.Undo);
        scene.Root.Children.ShouldBeEmpty();

        host.Apply(EditorHostCommand.Redo);
        SceneNode back = scene.Root.Children.ShouldHaveSingleItem();
        back.Id.ShouldBe(id);
        back.Entity.ShouldNotBeNull().TryGetValue("file", out string path).ShouldBeTrue();
        path.ShouldBe(DoorOpen);
    }

    [Fact]
    public void Dropped_over_a_face_it_sits_on_the_surface()
    {
        Scene scene = SceneWithASpeaker();
        SceneEditorHost host = NewHost(scene);
        SceneNode plate = scene.Root.CreateChild("Plate");
        plate.Brush = Brush.CreateBox(new Vector3(-8f, -1f, -8f), new Vector3(8f, 1f, 8f));
        scene.Camera.Position = new Vector3(0.5f, 8f, 4f);
        scene.Camera.LookAt(new Vector3(0.5f, 1f, 0.5f));

        host.InsertSound(DoorOpen);

        scene.Root.Children[^1].LocalPosition.Y.ShouldBe(1f, 0.001f);
    }

    [Fact]
    public void Dropped_over_nothing_it_lands_in_front_of_the_camera()
    {
        Scene scene = SceneWithASpeaker();
        SceneEditorHost host = NewHost(scene);
        scene.Camera.Position = new Vector3(0f, 2f, 0f);
        scene.Camera.LookAt(new Vector3(0f, 2f, -10f));

        SoundInsertReport report = host.InsertSound(DoorOpen, new Vector2(640f, 360f));

        report.Placed.ShouldBeTrue(report.Describe());
        Vector3 ahead = scene.Root.Children[0].LocalPosition - scene.Camera.Position;
        Vector3.Dot(Vector3.Normalize(ahead), scene.Camera.Forward).ShouldBeGreaterThan(0.99f);
        ahead.Length().ShouldBeGreaterThan(1f);
    }

    [Fact]
    public void A_path_is_stored_the_way_a_level_stores_it()
    {
        Scene scene = SceneWithASpeaker();
        SceneEditorHost host = NewHost(scene);

        host.InsertSound(@"Sounds\ambience\wind.wav").Placed.ShouldBeTrue();

        SceneNode node = scene.Root.Children[0];
        node.Name.ShouldBe("wind");
        node.Entity.ShouldNotBeNull().Keyvalues[0].Value.ShouldBe("Sounds/ambience/wind.wav");
    }

    [Fact]
    public void The_class_is_the_first_point_class_with_a_sound_file_among_its_settings()
    {
        Scene scene = SceneWith(
            new EntitySchema("test_door", placement: EntityPlacement.Brush, keyvalues: [Setting("opensound", KeyvalueType.AssetSound)]),
            new EntitySchema("test_light", keyvalues: [Setting("color", KeyvalueType.Color)]),
            new EntitySchema("test_radio", keyvalues: [Setting("station", KeyvalueType.AssetSound)]),
            new EntitySchema(Speaker, keyvalues: [Setting("file", KeyvalueType.AssetSound)]));

        SoundEntityBuilder.TryFindClass(scene.EntitySchemas, out EntitySchema? schema, out string setting).ShouldBeTrue();

        schema.ShouldNotBeNull().ClassName.ShouldBe("test_radio");
        setting.ShouldBe("station");
    }

    [Fact]
    public void With_several_classes_to_choose_from_the_report_names_the_one_it_took()
    {
        Scene scene = SceneWith(
            new EntitySchema("test_door", placement: EntityPlacement.Brush, keyvalues: [Setting("opensound", KeyvalueType.AssetSound)]),
            new EntitySchema("test_radio", keyvalues: [Setting("station", KeyvalueType.AssetSound)]),
            new EntitySchema(Speaker, keyvalues: [Setting("file", KeyvalueType.AssetSound)]));
        SceneEditorHost host = NewHost(scene);

        SoundInsertReport report = host.InsertSound(DoorOpen);

        // The door is made from geometry, so a file is never placed as one.
        SoundEntityBuilder.CountClasses(scene.EntitySchemas).ShouldBe(2);
        report.ClassName.ShouldBe("test_radio");
        report.OtherClasses.ShouldBe(1);
        report.Describe().ShouldBe(
            "Sounds/door_open.wav placed as 'door_open' of class test_radio. 1 other class takes a sound file too.");
        scene.Root.Children.ShouldHaveSingleItem().Entity.ShouldNotBeNull().ClassName.ShouldBe("test_radio");
    }

    [Fact]
    public void With_one_class_to_choose_from_the_report_has_nothing_to_add()
    {
        Scene scene = SceneWithASpeaker();
        SceneEditorHost host = NewHost(scene);

        SoundInsertReport report = host.InsertSound(DoorOpen);

        report.ClassName.ShouldBe(Speaker);
        report.OtherClasses.ShouldBe(0);
    }

    [Fact]
    public void A_project_with_no_class_that_plays_a_sound_file_refuses_the_drop()
    {
        Scene scene = SceneWith(new EntitySchema("test_light", keyvalues: [Setting("color", KeyvalueType.Color)]));
        SceneEditorHost host = NewHost(scene);

        SoundInsertReport report = host.InsertSound(DoorOpen);

        report.Placed.ShouldBeFalse();
        report.Describe().ShouldBe(
            "Sounds/door_open.wav was not placed: this project has no entity class that plays a sound file.");
        scene.Root.Children.ShouldBeEmpty();
        host.UndoDepth.ShouldBe(0);
    }

    [Theory]
    [InlineData("../../secrets.wav")]
    [InlineData(@"C:\elsewhere\door.wav")]
    public void A_path_outside_the_content_root_is_refused(string path)
    {
        Scene scene = SceneWithASpeaker();
        SceneEditorHost host = NewHost(scene);

        SoundInsertReport report = host.InsertSound(path);

        report.Refused.ShouldBe("that is not a content-relative path");
        scene.Root.Children.ShouldBeEmpty();
    }

    [Fact]
    public void A_blank_path_is_refused()
    {
        Scene scene = SceneWithASpeaker();
        SceneEditorHost host = NewHost(scene);

        host.InsertSound("   ").Refused.ShouldBe("no asset path was given");

        scene.Root.Children.ShouldBeEmpty();
    }

    [Fact]
    public void While_the_level_plays_the_drop_is_refused_in_the_words_a_model_drop_uses()
    {
        Scene scene = SceneWithASpeaker();
        SceneEditorHost host = NewHost(scene);
        host.Suspend();

        SoundInsertReport report = host.InsertSound(DoorOpen);

        report.Refused.ShouldBe(host.InsertModel("Models/crate.obj").Refused);
        report.Describe().ShouldBe("Sounds/door_open.wav was not placed: play mode owns the scene.");
        scene.Root.Children.ShouldBeEmpty();
    }
}
