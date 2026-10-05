using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>Entity payloads as property rows: which rows exist, their kinds, and the multi-selection merge.</summary>
public sealed class EntityPropertyRowTests
{
    // A catalogue can only be built from .sentdef bytes.
    private static EntitySchemaCatalog Catalog(params EntitySchema[] schemas) =>
        EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(schemas));

    private static KeyvalueDescriptor Kv(
        string name,
        KeyvalueType type,
        string value = "",
        uint flags = 0u,
        IReadOnlyList<(string Value, string Display)>? choices = null) =>
        new(name, "", "", value, type, KeyvalueWidget.Auto, float.NaN, float.NaN, flags,
            choices ?? KeyvalueDescriptor.NoChoices);

    private static SceneNode Placed(string className, params (string Key, string Value)[] keyvalues)
    {
        var data = new EntityData(className);
        foreach ((string key, string value) in keyvalues)
            data.SetValue(key, value);

        return new SceneNode(className) { Entity = data };
    }

    private static List<PropertyRow> Describe(SceneNode node, EntitySchemaCatalog? schemas = null)
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(node, rows, schemas);
        return rows;
    }

    private static PropertyRow Row(IReadOnlyList<PropertyRow> rows, string key)
    {
        foreach (PropertyRow row in rows)
        {
            if (row.Key == key)
                return row;
        }

        throw new Xunit.Sdk.XunitException($"No row carries the key '{key}'.");
    }

    [Fact]
    public void Two_classes_with_equally_long_schemas_are_not_the_same_shape()
    {
        // All keyvalue rows share one PropertyId, so ids alone call these the
        // same shape and the panel would pour speed's value into range's box.
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("a_thing", keyvalues:
            [
                Kv("speed", KeyvalueType.Float, "1"),
                Kv("loud", KeyvalueType.Bool, "0"),
            ]),
            new EntitySchema("b_thing", keyvalues:
            [
                Kv("range", KeyvalueType.Float, "1"),
                Kv("dark", KeyvalueType.Bool, "0"),
            ]));

        List<PropertyRow> first = Describe(Placed("a_thing"), catalog);
        List<PropertyRow> second = Describe(Placed("b_thing"), catalog);

        // Premise: the id sequences are identical.
        first.Count.ShouldBe(second.Count);
        for (int i = 0; i < first.Count; i++)
            first[i].Id.ShouldBe(second[i].Id);

        var shape = new PropertyRowShape();
        shape.CaptureFrom(first);

        shape.Matches(first).ShouldBeTrue();
        shape.Matches(second).ShouldBeFalse();
    }

    [Fact]
    public void One_class_keeps_its_shape_across_publishes_while_its_values_move()
    {
        // Otherwise the panel rebuilds on every frame of a drag.
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("mover", keyvalues: [Kv("speed", KeyvalueType.Float, "1")]));

        var shape = new PropertyRowShape();
        shape.CaptureFrom(Describe(Placed("mover", ("speed", "1")), catalog));

        shape.Matches(Describe(Placed("mover", ("speed", "97.5")), catalog)).ShouldBeTrue();
    }

    [Fact]
    public void The_classname_is_shown_and_cannot_be_edited()
    {
        PropertyRow row = Describe(Placed("light_omni"))
            .Find(r => r.Id == PropertyId.EntityClassname);
        row.Group.ShouldBe(NodeInspector.EntityGroup);
        row.Kind.ShouldBe(PropertyKind.ReadOnlyText);
        row.Text.ShouldBe("light_omni");
        row.IsEditable.ShouldBeFalse();
    }

    [Fact]
    public void A_node_with_no_entity_grows_no_entity_rows()
    {
        Describe(new SceneNode("plain")).ShouldNotContain(r => r.Group == NodeInspector.EntityGroup);
    }

    [Theory]
    [InlineData(KeyvalueType.Bool, "1", PropertyKind.Boolean)]
    [InlineData(KeyvalueType.Int, "7", PropertyKind.Number)]
    [InlineData(KeyvalueType.Float, "2.5", PropertyKind.Number)]
    [InlineData(KeyvalueType.Distance, "30", PropertyKind.Number)]
    [InlineData(KeyvalueType.Vec3, "1 2 3", PropertyKind.Vector3)]
    [InlineData(KeyvalueType.Angles, "0 90 0", PropertyKind.Vector3)]
    [InlineData(KeyvalueType.Color, "1 0.5 0", PropertyKind.Color)]
    [InlineData(KeyvalueType.String, "hello", PropertyKind.Text)]
    [InlineData(KeyvalueType.TargetName, "door", PropertyKind.Target)]
    // NodeRef is a GUID on the wire; the name picker would write a name there.
    [InlineData(KeyvalueType.NodeRef, "0f8fad5b-d9cb-469f-a165-70867728950e", PropertyKind.Text)]
    [InlineData(KeyvalueType.AssetModel, "Models/x.obj", PropertyKind.Asset)]
    [InlineData(KeyvalueType.AssetMaterial, "Materials/x.spectramat", PropertyKind.Asset)]
    [InlineData(KeyvalueType.AssetTexture, "Textures/x.png", PropertyKind.Asset)]
    [InlineData(KeyvalueType.AssetSound, "Sounds/x.wav", PropertyKind.Asset)]
    [InlineData(KeyvalueType.Flags, "3", PropertyKind.Text)]
    [InlineData(KeyvalueType.Vec2, "1 2", PropertyKind.Text)]
    public void A_declared_type_picks_the_editor(KeyvalueType type, string value, PropertyKind kind)
    {
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("thing", keyvalues: [Kv("p", type)]));

        Row(Describe(Placed("thing", ("p", value)), catalog), "p").Kind.ShouldBe(kind);
    }

    [Theory]
    [InlineData(KeyvalueType.AssetModel, AssetKind.Model)]
    [InlineData(KeyvalueType.AssetMaterial, AssetKind.Material)]
    [InlineData(KeyvalueType.AssetTexture, AssetKind.Texture)]
    [InlineData(KeyvalueType.AssetSound, AssetKind.Sound)]
    public void An_asset_row_says_which_kind_of_file_it_wants(KeyvalueType type, AssetKind kind)
    {
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("thing", keyvalues: [Kv("p", type)]));

        Row(Describe(Placed("thing", ("p", "x")), catalog), "p").Asset.ShouldBe(kind);
    }

    [Fact]
    public void A_choices_row_offers_the_wire_tokens_rather_than_the_display_names()
    {
        // The dropdown matches by text and the row's value is the wire string.
        EntitySchemaCatalog catalog = Catalog(new EntitySchema("door", keyvalues:
        [
            Kv("movedir", KeyvalueType.Choices, "up", choices: [("up", "Up"), ("down", "Down")]),
        ]));

        PropertyRow row = Row(Describe(Placed("door"), catalog), "movedir");
        row.Kind.ShouldBe(PropertyKind.Choice);
        row.Text.ShouldBe("up");
        row.Choices.ShouldBe(["up", "down"]);
    }

    [Fact]
    public void An_angles_row_says_it_is_degrees()
    {
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("thing", keyvalues: [Kv("angles", KeyvalueType.Angles, "0 0 0")]));

        Row(Describe(Placed("thing"), catalog), "angles").Unit.ShouldBe("deg");
    }

    [Fact]
    public void A_distance_row_says_it_is_units_and_a_float_row_says_nothing()
    {
        EntitySchemaCatalog catalog = Catalog(new EntitySchema("thing", keyvalues:
        [
            Kv("reach", KeyvalueType.Distance, "30"),
            Kv("speed", KeyvalueType.Float, "30"),
        ]));

        List<PropertyRow> rows = Describe(Placed("thing"), catalog);
        Row(rows, "reach").Unit.ShouldBe("su");
        Row(rows, "reach").Number.ShouldBe(30f);
        Row(rows, "speed").Unit.ShouldBe("");
    }

    [Fact]
    public void A_value_the_declared_type_cannot_carry_degrades_to_text()
    {
        // A typed row would parse it as zero and commit the zero.
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("thing", keyvalues: [Kv("size", KeyvalueType.Vec3, "1 1 1")]));

        PropertyRow row = Row(Describe(Placed("thing", ("size", "wide")), catalog), "size");
        row.Kind.ShouldBe(PropertyKind.Text);
        row.Text.ShouldBe("wide");
    }

    [Fact]
    public void A_read_only_descriptor_is_shown_and_not_edited()
    {
        EntitySchemaCatalog catalog = Catalog(new EntitySchema("thing", keyvalues:
        [
            Kv("build", KeyvalueType.String, "42", KeyvalueFlags.ReadOnly),
        ]));

        Row(Describe(Placed("thing"), catalog), "build").IsEditable.ShouldBeFalse();
    }

    [Fact]
    public void A_hidden_descriptor_gets_no_row_even_when_the_node_stores_it()
    {
        // Must not come back as an unknown-key row either.
        EntitySchemaCatalog catalog = Catalog(new EntitySchema("thing", keyvalues:
        [
            Kv("secret", KeyvalueType.String, "", KeyvalueFlags.HideInEditor),
            Kv("shown", KeyvalueType.String),
        ]));

        List<PropertyRow> rows = Describe(Placed("thing", ("secret", "x"), ("shown", "y")), catalog);
        rows.ShouldNotContain(r => r.Key == "secret");
        rows.ShouldContain(r => r.Key == "shown");
    }

    [Fact]
    public void An_unauthored_key_shows_the_declared_default()
    {
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("thing", keyvalues: [Kv("speed", KeyvalueType.Float, "100")]));

        Row(Describe(Placed("thing"), catalog), "speed").Number.ShouldBe(100f);
        Row(Describe(Placed("thing", ("speed", "3")), catalog), "speed").Number.ShouldBe(3f);
    }

    [Fact]
    public void Declared_rows_follow_the_schemas_order_not_the_authored_one()
    {
        EntitySchemaCatalog catalog = Catalog(new EntitySchema("thing", keyvalues:
        [
            Kv("zulu", KeyvalueType.String),
            Kv("alpha", KeyvalueType.String),
            Kv("mike", KeyvalueType.String),
        ]));

        List<PropertyRow> rows = Describe(
            Placed("thing", ("mike", "3"), ("alpha", "2"), ("zulu", "1")), catalog);

        KeysOf(rows).ShouldBe(["zulu", "alpha", "mike"]);
    }

    [Fact]
    public void A_placeholder_entity_shows_every_key_it_carries()
    {
        // No schema for this class: rows come out in authored order.
        List<PropertyRow> rows = Describe(
            Placed("game_from_another_engine", ("wait", "3"), ("target", "door"), ("aa", "1")));

        KeysOf(rows).ShouldBe(["wait", "target", "aa"]);
        rows.ShouldAllBe(r => r.Key == "" || r.Kind == PropertyKind.Text);
    }

    [Fact]
    public void Keys_the_schema_does_not_name_are_kept_after_the_ones_it_does()
    {
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("thing", keyvalues: [Kv("speed", KeyvalueType.Float, "1")]));

        List<PropertyRow> rows = Describe(
            Placed("thing", ("leftover", "7"), ("speed", "2")), catalog);

        KeysOf(rows).ShouldBe(["speed", "leftover"]);
        Row(rows, "leftover").Kind.ShouldBe(PropertyKind.Text);
    }

    [Fact]
    public void A_duplicated_key_produces_one_row()
    {
        // A hand-written file may repeat a key. First wins, as in the entity bind.
        var data = new EntityData("thing");
        data.Keyvalues.Add(new KeyValuePair<string, string>("wait", "1"));
        data.Keyvalues.Add(new KeyValuePair<string, string>("wait", "2"));
        var node = new SceneNode("thing") { Entity = data };

        List<PropertyRow> rows = Describe(node);
        KeysOf(rows).ShouldBe(["wait"]);
        Row(rows, "wait").Text.ShouldBe("1");
    }

    [Fact]
    public void A_selection_shows_the_union_of_its_entities_keys()
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(
            [
                Placed("a", ("shared", "1"), ("only_a", "x")),
                Placed("b", ("shared", "1"), ("only_b", "y")),
            ],
            rows);

        Row(rows, "shared").PresentCount.ShouldBe(2);
        Row(rows, "only_a").PresentCount.ShouldBe(1);
        Row(rows, "only_a").IsPartial.ShouldBeTrue();
        Row(rows, "only_b").PresentCount.ShouldBe(1);
    }

    [Fact]
    public void A_merged_key_reports_mixing_per_axis()
    {
        EntitySchemaCatalog catalog = Catalog(
            new EntitySchema("thing", keyvalues: [Kv("offset", KeyvalueType.Vec3, "0 0 0")]));

        var rows = new List<PropertyRow>();
        NodeInspector.Describe(
            [
                Placed("thing", ("offset", "1 2 3")),
                Placed("thing", ("offset", "1 9 3")),
            ],
            rows,
            catalog);

        Row(rows, "offset").MixedAxes.ShouldBe(PropertyAxes.Y);
    }

    [Fact]
    public void Merged_keys_are_compared_as_exact_strings()
    {
        // "1" and "1.0" are different spellings; a bulk edit would overwrite one.
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(
            [Placed("thing", ("wait", "1")), Placed("thing", ("wait", "1.0"))],
            rows);

        Row(rows, "wait").IsMixed.ShouldBeTrue();
    }

    [Fact]
    public void A_merged_selection_keeps_one_row_per_key()
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(
            [Placed("thing", ("a", "1"), ("b", "2")), Placed("thing", ("a", "1"), ("b", "2"))],
            rows);

        KeysOf(rows).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void A_merged_selection_keeps_schema_order_within_the_entity_section()
    {
        EntitySchemaCatalog catalog = Catalog(new EntitySchema("thing", keyvalues:
        [
            Kv("zulu", KeyvalueType.String),
            Kv("alpha", KeyvalueType.String),
        ]));

        var rows = new List<PropertyRow>();
        NodeInspector.Describe([Placed("thing"), Placed("thing")], rows, catalog);

        KeysOf(rows).ShouldBe(["zulu", "alpha"]);
    }

    [Fact]
    public void The_entity_section_stays_one_contiguous_run()
    {
        // The panel groups by runs of equal group names.
        var light = new SceneNode("lamp")
        {
            Light = new Light { Kind = LightKind.Point },
            Entity = new EntityData("light_omni"),
        };
        light.Entity!.SetValue("brightness", "2");

        List<PropertyRow> rows = Describe(light);

        int first = rows.FindIndex(r => r.Group == NodeInspector.EntityGroup);
        int last = rows.FindLastIndex(r => r.Group == NodeInspector.EntityGroup);
        int count = 0;
        foreach (PropertyRow row in rows)
        {
            if (row.Group == NodeInspector.EntityGroup)
                count++;
        }

        first.ShouldBeGreaterThanOrEqualTo(0);
        (last - first + 1).ShouldBe(count);
    }

    [Fact]
    public void Every_row_that_is_not_an_entity_keyvalue_carries_an_empty_key()
    {
        var node = new SceneNode("lamp")
        {
            Light = new Light { Kind = LightKind.Point },
            LocalPosition = new Vector3(1f, 2f, 3f),
        };

        foreach (PropertyRow row in Describe(node))
            row.Key.ShouldBe("");
    }

    private static List<string> KeysOf(IReadOnlyList<PropertyRow> rows)
    {
        var keys = new List<string>();
        foreach (PropertyRow row in rows)
        {
            if (row.Id == PropertyId.EntityKeyvalue)
                keys.Add(row.Key);
        }

        return keys;
    }
}
