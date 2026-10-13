using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="AuthoredAttributes"/>: a keyvalue of an entity as a value of
/// the type its class's schema gives it, which is what a script's
/// GetAttribute reads.
/// </summary>
public sealed class AuthoredAttributesTests
{
    private const string Class = "sign";

    private readonly EntityCatalog _classes = Classes();
    private readonly SceneNode _node = new("Sign") { Entity = new EntityData(Class) };

    [Theory]
    [InlineData("lit", "0", false)]
    [InlineData("lit", "1", true)]
    public void A_flag_reads_as_a_boolean(string name, string authored, bool value)
    {
        Read(name, authored).ShouldBe((KeyvalueType.Bool, ParameterValue.Of(value)));
    }

    [Theory]
    [InlineData("count", KeyvalueType.Int, "-42", -42d)]
    [InlineData("count", KeyvalueType.Int, "2147483647", 2147483647d)]
    [InlineData("speed", KeyvalueType.Float, "0.1", 0.1d)]
    [InlineData("speed", KeyvalueType.Float, " 2.5 ", 2.5d)]
    [InlineData("reach", KeyvalueType.Distance, "12.75", 12.75d)]
    [InlineData("bits", KeyvalueType.Flags, "4294967295", 4294967295d)]
    public void A_whole_number_a_float_a_distance_and_a_bit_set_read_as_the_number_the_text_spells(
        string name, KeyvalueType type, string authored, double value)
    {
        Read(name, authored).ShouldBe((type, ParameterValue.Of(value)));
    }

    [Theory]
    [InlineData("label", KeyvalueType.String, "Way out")]
    [InlineData("label", KeyvalueType.String, "")]
    [InlineData("target", KeyvalueType.TargetName, "Door*")]
    [InlineData("model", KeyvalueType.AssetModel, "Models/sign.gltf")]
    [InlineData("sound", KeyvalueType.AssetSound, "Sounds/buzz.wav")]
    [InlineData("mode", KeyvalueType.Choices, "blink")]
    public void Text_a_name_a_path_and_a_choice_read_as_their_text(string name, KeyvalueType type, string authored)
    {
        Read(name, authored).ShouldBe((type, ParameterValue.Of(authored)));
    }

    [Theory]
    [InlineData("offset", KeyvalueType.Vec3, "1 -2.5 3", 1f, -2.5f, 3f)]
    [InlineData("facing", KeyvalueType.Angles, "0 90 0", 0f, 90f, 0f)]
    [InlineData("tint", KeyvalueType.Color, "1 0.5 0", 1f, 0.5f, 0f)]
    public void Three_numbers_an_angle_and_a_colour_read_as_a_vector(
        string name, KeyvalueType type, string authored, float x, float y, float z)
    {
        Read(name, authored).ShouldBe((type, ParameterValue.Of(new Vector3(x, y, z))));
    }

    // The script API has no value of two or of four numbers.
    [Theory]
    [InlineData("size", KeyvalueType.Vec2, "3 4")]
    [InlineData("plane", KeyvalueType.Vec4, "0 1 0 -2")]
    public void Two_numbers_and_four_read_as_their_text(string name, KeyvalueType type, string authored)
    {
        Read(name, authored).ShouldBe((type, ParameterValue.Of(authored)));
    }

    [Fact]
    public void A_reference_reads_as_the_id_of_its_node_and_an_empty_one_as_no_value()
    {
        var id = Guid.NewGuid();

        Read("partner", KeyvalueWire.Format(id)).ShouldBe((KeyvalueType.NodeRef, ParameterValue.Of(id)));
        Read("partner", "").ShouldBe((KeyvalueType.NodeRef, ParameterValue.None));
    }

    [Fact]
    public void A_keyvalue_the_node_leaves_out_reads_as_the_class_gives_it()
    {
        AuthoredAttributes.TryRead(_node, _classes, "speed", out KeyvalueType type, out ParameterValue value).ShouldBeTrue();

        type.ShouldBe(KeyvalueType.Float);
        value.ShouldBe(ParameterValue.Of(1.5));
    }

    // The entity keeps its own value when it cannot read the node's, so that
    // is what it runs with.
    [Theory]
    [InlineData("speed", "fast", 1.5d)]
    [InlineData("speed", "1,5", 1.5d)]
    [InlineData("count", "2.5", 3d)]
    public void A_value_its_type_cannot_read_reads_as_the_class_gives_it(string name, string authored, double value)
    {
        Read(name, authored).Value.ShouldBe(ParameterValue.Of(value));
    }

    [Fact]
    public void A_keyvalue_the_class_does_not_declare_reads_as_the_nodes_text()
    {
        Read("note", "kept as it is").ShouldBe((KeyvalueType.String, ParameterValue.Of("kept as it is")));
    }

    [Fact]
    public void A_name_neither_the_node_nor_its_class_has_reads_as_nothing()
    {
        AuthoredAttributes.TryRead(_node, _classes, "Speed", out _, out ParameterValue value).ShouldBeFalse();

        value.ShouldBe(ParameterValue.None);
    }

    // The entity is handed every pair in order and keeps what it could read
    // last. A map and a pack both keep a name written twice.
    [Theory]
    [InlineData("1", "2", 2d)]
    [InlineData("1", "two", 1d)]
    [InlineData("one", "2", 2d)]
    [InlineData("one", "two", 3d)]
    public void Of_two_values_under_one_declared_name_the_last_its_type_can_read_is_read(
        string first, string second, double value)
    {
        EntityData entity = _node.Entity.ShouldNotBeNull();
        entity.Keyvalues.Add(new("count", first));
        entity.Keyvalues.Add(new("label", "between"));
        entity.Keyvalues.Add(new("count", second));

        AuthoredAttributes.TryRead(_node, _classes, "count", out _, out ParameterValue read).ShouldBeTrue();

        read.ShouldBe(ParameterValue.Of(value));
    }

    // No entity reads such a name. EntityData.TryGetValue gives the first.
    [Fact]
    public void Of_two_values_under_a_name_the_class_does_not_declare_the_first_is_read()
    {
        EntityData entity = _node.Entity.ShouldNotBeNull();
        entity.Keyvalues.Add(new("note", "first"));
        entity.Keyvalues.Add(new("note", "second"));

        AuthoredAttributes.TryRead(_node, _classes, "note", out _, out ParameterValue read).ShouldBeTrue();

        read.ShouldBe(ParameterValue.Of("first"));
    }

    [Fact]
    public void A_class_this_build_does_not_have_reads_every_keyvalue_as_text()
    {
        var node = new SceneNode("Odd") { Entity = new EntityData("from_another_game") };
        node.Entity.SetValue("speed", "3");

        AuthoredAttributes.TryRead(node, _classes, "speed", out KeyvalueType type, out ParameterValue value).ShouldBeTrue();

        type.ShouldBe(KeyvalueType.String);
        value.ShouldBe(ParameterValue.Of("3"));
    }

    [Fact]
    public void A_node_with_no_entity_has_no_attribute()
    {
        var plain = new SceneNode("Plain");
        var unnamed = new SceneNode("Unnamed") { Entity = new EntityData() };
        unnamed.Entity.SetValue("speed", "3");

        AuthoredAttributes.TryRead(plain, _classes, "speed", out _, out _).ShouldBeFalse();
        AuthoredAttributes.TryRead(unnamed, _classes, "speed", out _, out _).ShouldBeFalse();
    }

    private (KeyvalueType Type, ParameterValue Value) Read(string name, string authored)
    {
        _node.Entity.ShouldNotBeNull().SetValue(name, authored);

        AuthoredAttributes.TryRead(_node, _classes, name, out KeyvalueType type, out ParameterValue value).ShouldBeTrue();
        return (type, value);
    }

    private static EntityCatalog Classes()
    {
        var catalog = new EntityCatalog();
        catalog.Add(
            new EntitySchema(Class, keyvalues:
            [
                Declared("lit", KeyvalueType.Bool, "1"),
                Declared("count", KeyvalueType.Int, "3"),
                Declared("speed", KeyvalueType.Float, "1.5"),
                Declared("reach", KeyvalueType.Distance, "8"),
                Declared("bits", KeyvalueType.Flags, "0"),
                Declared("label", KeyvalueType.String, "Sign"),
                Declared("target", KeyvalueType.TargetName, ""),
                Declared("model", KeyvalueType.AssetModel, ""),
                Declared("sound", KeyvalueType.AssetSound, ""),
                Declared("mode", KeyvalueType.Choices, "steady"),
                Declared("offset", KeyvalueType.Vec3, "0 0 0"),
                Declared("facing", KeyvalueType.Angles, "0 0 0"),
                Declared("tint", KeyvalueType.Color, "1 1 1"),
                Declared("size", KeyvalueType.Vec2, "1 1"),
                Declared("plane", KeyvalueType.Vec4, "0 1 0 0"),
                Declared("partner", KeyvalueType.NodeRef, ""),
            ]),
            () => new RelayEntity());
        return catalog;
    }

    private static KeyvalueDescriptor Declared(string name, KeyvalueType type, string value) =>
        new(name, "", "", value, type, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u, KeyvalueDescriptor.NoChoices);
}
