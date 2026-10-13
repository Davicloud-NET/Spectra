using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="AuthoredAttributes.AddNames"/>: the names a script's
/// GetAttributes asks an entity for.
/// </summary>
public sealed class AuthoredAttributeNamesTests
{
    private const string Class = "sign";

    private readonly EntityCatalog _classes = Classes();

    [Fact]
    public void The_names_a_class_declares_come_first_and_then_those_only_the_node_has()
    {
        var node = new SceneNode("Sign") { Entity = new EntityData(Class) };
        node.Entity.SetValue("note", "kept as it is");
        node.Entity.SetValue("speed", "3");
        node.Entity.Keyvalues.Add(new("note", "a second one"));
        node.Entity.SetValue("another", "1");

        Names(node).ShouldBe(["lit", "speed", "label", "note", "another"]);
    }

    // Names are added to what the list holds: a name that is there already
    // from somebody else is no name of this node's.
    [Fact]
    public void Names_are_added_after_what_the_list_holds_already()
    {
        var node = new SceneNode("Sign") { Entity = new EntityData(Class) };
        node.Entity.SetValue("note", "kept as it is");
        var names = new List<string> { "note", "speed" };

        AuthoredAttributes.AddNames(node, _classes, names);

        names.ShouldBe(["note", "speed", "lit", "speed", "label", "note"]);
    }

    [Fact]
    public void A_class_this_build_does_not_have_gives_the_names_the_node_has()
    {
        var node = new SceneNode("Odd") { Entity = new EntityData("from_another_game") };
        node.Entity.SetValue("speed", "3");
        node.Entity.SetValue("lit", "1");

        Names(node).ShouldBe(["speed", "lit"]);
    }

    [Fact]
    public void A_node_with_no_entity_has_no_names()
    {
        var unnamed = new SceneNode("Unnamed") { Entity = new EntityData() };
        unnamed.Entity.SetValue("speed", "3");

        Names(new SceneNode("Plain")).ShouldBeEmpty();
        Names(unnamed).ShouldBeEmpty();
    }

    private List<string> Names(SceneNode node)
    {
        var names = new List<string>();
        AuthoredAttributes.AddNames(node, _classes, names);
        return names;
    }

    private static EntityCatalog Classes()
    {
        var catalog = new EntityCatalog();
        catalog.Add(
            new EntitySchema(Class, keyvalues:
            [
                Declared("lit", KeyvalueType.Bool, "1"),
                Declared("speed", KeyvalueType.Float, "1.5"),
                Declared("label", KeyvalueType.String, "Sign"),
            ]),
            () => new RelayEntity());
        return catalog;
    }

    private static KeyvalueDescriptor Declared(string name, KeyvalueType type, string value) =>
        new(name, "", "", value, type, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u, KeyvalueDescriptor.NoChoices);
}
