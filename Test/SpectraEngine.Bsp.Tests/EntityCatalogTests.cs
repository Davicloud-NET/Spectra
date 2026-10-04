using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

/// <summary><see cref="EntityCatalog"/> registration, ordering and freezing.</summary>
public sealed class EntityCatalogTests
{
    [Fact]
    public void Enumeration_is_ordered_by_class_name_and_not_by_registration_order()
    {
        // Classes register from module initializers, whose order the loader
        // picks. The exported schema must be byte-identical across runs.
        var catalog = new EntityCatalog();
        catalog.Add(new EntitySchema("logic_relay"), () => new PlaceholderEntity());
        catalog.Add(new EntitySchema("func_door"), () => new PlaceholderEntity());
        catalog.Add(new EntitySchema("Ambient_generic"), () => new PlaceholderEntity());

        string[] names = catalog.Schemas.Select(s => s.ClassName).ToArray();

        // Ordinal: uppercase sorts first. A culture-aware order varies by machine.
        names.ShouldBe(["Ambient_generic", "func_door", "logic_relay"]);
    }

    [Fact]
    public void Registering_a_class_name_twice_throws_and_names_it()
    {
        var catalog = new EntityCatalog();
        catalog.Add(new EntitySchema("func_door"), () => new PlaceholderEntity());

        var thrown = Should.Throw<InvalidOperationException>(
            () => catalog.Add(new EntitySchema("func_door"), () => new PlaceholderEntity()));

        thrown.Message.ShouldContain("func_door");
    }

    [Fact]
    public void A_catalogue_freezes_on_its_first_read_and_refuses_later_registrations()
    {
        var catalog = new EntityCatalog();
        catalog.Add(new EntitySchema("func_door"), () => new PlaceholderEntity());
        catalog.IsFrozen.ShouldBeFalse();

        catalog.TryCreate("func_door", out Entity? built).ShouldBeTrue();
        built.ShouldBeOfType<PlaceholderEntity>();
        catalog.IsFrozen.ShouldBeTrue();

        var thrown = Should.Throw<InvalidOperationException>(
            () => catalog.Add(new EntitySchema("func_button"), () => new PlaceholderEntity()));

        thrown.Message.ShouldContain("func_button");
    }

    [Fact]
    public void An_unregistered_class_name_builds_nothing_rather_than_throwing()
    {
        var catalog = new EntityCatalog();

        catalog.TryCreate("func_nothing_here", out Entity? built).ShouldBeFalse();
        built.ShouldBeNull();
        catalog.TryGetSchema("func_nothing_here", out EntitySchema? schema).ShouldBeFalse();
        schema.ShouldBeNull();
    }
}
