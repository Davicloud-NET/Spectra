using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The Sound setting of a sound entity in the Properties panel: a file picked
/// from the project, with a play button beside it.
/// </summary>
public sealed class SoundRowTests
{
    private const string ClassName = "test_sound";
    private const string Open = "Sounds/door_open.wav";

    // Shaped as point_sound's sound setting, and learned from .sentdef bytes
    // as the editor learns any class. Not the built-in class itself: loading
    // it registers into the shared catalogue, which other tests here have read.
    private static readonly EntitySchemaCatalog Schemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema(ClassName, keyvalues:
        [
            new KeyvalueDescriptor(
                "sound", "Sound", "The sound file to play.", "", KeyvalueType.AssetSound, KeyvalueWidget.Auto,
                float.NaN, float.NaN, 0u, KeyvalueDescriptor.NoChoices),
        ]),
    ]));

    private sealed class Rig
    {
        public List<PropertyEdit> Edits { get; } = [];
        public PropertyPanelModel Panel { get; }

        public Rig() => Panel = new PropertyPanelModel(Edits.Add, _ => { }, _ => { });

        // Rows as the engine publishes them for the selected sounds.
        public PropertyRowModel Publish(params string?[] sounds)
        {
            var nodes = new List<SceneNode>();
            foreach (string? sound in sounds)
                nodes.Add(Placed(sound));

            var rows = new List<PropertyRow>();
            NodeInspector.Describe(nodes, rows, Schemas);
            Panel.Apply(rows, nodes.Count);

            return Panel.Groups.SelectMany(group => group.Rows).Single(row => row.Key == "sound");
        }
    }

    // Null leaves the setting unwritten, as a fresh entity has it.
    private static SceneNode Placed(string? sound)
    {
        var entity = new EntityData(ClassName);
        if (sound is not null)
            entity.SetValue("sound", sound);

        return new SceneNode("DoorSound") { Entity = entity };
    }

    [Fact]
    public void The_sound_setting_is_published_as_an_asset_row_of_kind_sound()
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(Placed(Open), rows, Schemas);

        PropertyRow row = rows.Single(candidate => candidate.Key == "sound");

        row.Kind.ShouldBe(PropertyKind.Asset);
        row.Asset.ShouldBe(AssetKind.Sound);
        row.Name.ShouldBe("Sound");
        row.Text.ShouldBe(Open);
        row.Id.ShouldBe(PropertyId.EntityKeyvalue);
    }

    [Fact]
    public void The_row_shows_the_files_name_and_can_play_it()
    {
        PropertyRowModel row = new Rig().Publish(Open);

        row.IsAsset.ShouldBeTrue();
        row.AssetKind.ShouldBe(AssetKind.Sound);
        row.AssetLabel.ShouldBe("door_open");
        row.AssetPath.ShouldBe(Open);
        row.CanPreview.ShouldBeTrue();
    }

    [Fact]
    public void The_row_offers_the_projects_sounds()
    {
        PropertyRowModel row = new Rig().Publish(Open);

        AssetCatalog.KindFor(row.AssetKind).ShouldBe(ContentKind.Sound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_row_with_no_file_says_none_and_has_nothing_to_play(string? sound)
    {
        PropertyRowModel row = new Rig().Publish(sound);

        // No sound stands in for an empty one, so it is not "(default)".
        row.AssetLabel.ShouldBe("(none)");
        row.CanPreview.ShouldBeFalse();
    }

    [Fact]
    public void Sounds_that_differ_across_a_selection_have_nothing_to_play()
    {
        PropertyRowModel row = new Rig().Publish(Open, "Sounds/door_close.wav");

        row.AssetLabel.ShouldBe("(mixed)");
        row.CanPreview.ShouldBeFalse();
    }

    [Fact]
    public void Picking_a_file_posts_the_path_a_level_stores()
    {
        var rig = new Rig();
        PropertyRowModel row = rig.Publish(Open);

        row.PickAsset("Sounds/door_close.wav");

        PropertyEdit edit = rig.Edits.ShouldHaveSingleItem();
        edit.Id.ShouldBe(PropertyId.EntityKeyvalue);
        edit.Key.ShouldBe("sound");
        edit.Text.ShouldBe("Sounds/door_close.wav");
    }

    [Fact]
    public void Picking_none_posts_an_empty_path()
    {
        var rig = new Rig();
        PropertyRowModel row = rig.Publish(Open);

        row.PickAsset("");

        rig.Edits.ShouldHaveSingleItem().Text.ShouldBe("");
    }

    [Fact]
    public void The_play_button_follows_the_file_the_row_holds()
    {
        var rig = new Rig();
        PropertyRowModel row = rig.Publish((string?)null);
        var raised = new List<string?>();
        row.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        rig.Publish(Open);

        row.CanPreview.ShouldBeTrue();
        raised.ShouldContain(nameof(PropertyRowModel.CanPreview));
    }

    [Fact]
    public void A_row_of_another_kind_has_no_play_button()
    {
        var panel = new PropertyPanelModel(_ => { }, _ => { }, _ => { });
        panel.Apply(
        [
            new PropertyRow
            {
                Group = "Material", Name = "Material", Id = PropertyId.BrushMaterial,
                Kind = PropertyKind.Asset, Asset = AssetKind.Material,
                Text = "Materials/wall.spectramat", Choices = [], PresentCount = 1, SelectionCount = 1,
            },
        ], 1);

        panel.Groups[0].Rows[0].CanPreview.ShouldBeFalse();
    }
}
