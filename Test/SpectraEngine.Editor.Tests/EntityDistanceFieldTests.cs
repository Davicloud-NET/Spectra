using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

/// <summary>A distance setting in the Properties panel.</summary>
public sealed class EntityDistanceFieldTests
{
    private const string Sound = "test_sound";

    // Shaped as point_sound's two distances, and learned from .sentdef bytes
    // as the editor learns any class.
    private static readonly EntitySchemaCatalog Schemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema(Sound, keyvalues:
        [
            Distance("mindistance", "Minimum distance", "2"),
            Distance("maxdistance", "Maximum distance", "30"),
        ]),
    ]));

    private static KeyvalueDescriptor Distance(string name, string display, string value) =>
        new(name, display, "", value, KeyvalueType.Distance, KeyvalueWidget.Auto, 0f, float.NaN, 0u,
            KeyvalueDescriptor.NoChoices);

    private sealed class Rig
    {
        public List<PropertyEdit> Edits { get; } = [];
        public PropertyPanelModel Panel { get; }

        public Rig() => Panel = new PropertyPanelModel(Edits.Add, _ => { }, _ => { });

        // Rows as the engine publishes them for one selected sound.
        public PropertyRowModel Publish(string key, params (string Key, string Value)[] written)
        {
            var data = new EntityData(Sound);
            foreach ((string name, string value) in written)
                data.SetValue(name, value);

            var rows = new List<PropertyRow>();
            NodeInspector.Describe(new SceneNode("hum") { Entity = data }, rows, Schemas);
            Panel.Apply(rows, 1);

            return Panel.Groups.SelectMany(group => group.Rows).Single(row => row.Key == key);
        }
    }

    [Fact]
    public void A_distance_setting_gets_one_number_field_in_units()
    {
        PropertyRowModel row = new Rig().Publish("maxdistance", ("maxdistance", "12.5"));

        row.Kind.ShouldBe(PropertyKind.Number);
        row.Name.ShouldBe("Maximum distance");
        row.IsScalar.ShouldBeTrue();
        row.Fields.Count.ShouldBe(1);
        row.Fields[0].Text.ShouldBe("12.5");
        row.Fields[0].Unit.ShouldBe("su");
    }

    [Fact]
    public void A_distance_the_level_does_not_write_shows_the_class_default()
    {
        var rig = new Rig();

        rig.Publish("mindistance").Fields[0].Text.ShouldBe("2");
        rig.Publish("maxdistance").Fields[0].Text.ShouldBe("30");
    }

    [Fact]
    public void A_typed_distance_is_posted_as_the_settings_text()
    {
        var rig = new Rig();
        PropertyFieldModel field = rig.Publish("maxdistance").Fields[0];

        field.BeginEdit();
        field.Text = "45.5";
        field.Commit();

        rig.Edits.Count.ShouldBe(1);
        rig.Edits[0].Id.ShouldBe(PropertyId.EntityKeyvalue);
        rig.Edits[0].Key.ShouldBe("maxdistance");
        rig.Edits[0].Text.ShouldBe("45.5");
    }

    [Fact]
    public void A_distance_that_is_not_a_number_is_shown_as_the_text_it_is()
    {
        PropertyRowModel row = new Rig().Publish("maxdistance", ("maxdistance", "far"));

        row.Kind.ShouldBe(PropertyKind.Text);
        row.Fields[0].Text.ShouldBe("far");
    }
}
