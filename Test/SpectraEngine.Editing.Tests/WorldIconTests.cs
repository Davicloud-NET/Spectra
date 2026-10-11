using System;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Which icon file stands for an entity class or a kind of light, and what a
/// class gets when it names none or a wrong one.
/// </summary>
public sealed class WorldIconTests
{
    // Classes that exist only as .sentdef bytes, the way the editor learns any class.
    private static readonly EntitySchemaCatalog Schemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema("test_door", icon: "door"),
        new EntitySchema("test_once", icon: "trigger-once"),
        new EntitySchema("test_plain"),
        new EntitySchema("test_wrong", icon: "no-such-drawing"),
        new EntitySchema("test_odd", icon: "../Door"),
    ]));

    [Theory]
    [InlineData("door", "IconEntityDoor")]
    [InlineData("trigger-once", "IconEntityTriggerOnce")]
    [InlineData("player-start", "IconEntityPlayerStart")]
    [InlineData("a-b-c", "IconEntityABC")]
    [InlineData("", "IconEntity")]
    public void An_icon_name_becomes_a_key_in_pascal_case_with_the_hyphens_gone(string name, string key)
    {
        WorldIcons.KeyForName(name).ShouldBe(key);
    }

    [Fact]
    public void A_class_gets_the_file_its_icon_names()
    {
        WorldIcons.ForEntity(Schemas, "test_door").ShouldBe("IconEntityDoor");
        WorldIcons.ForEntity(Schemas, "test_once").ShouldBe("IconEntityTriggerOnce");
    }

    [Fact]
    public void A_class_that_names_no_icon_gets_the_plain_entity_icon()
    {
        WorldIcons.ForEntity(Schemas, "test_plain").ShouldBe(WorldIcons.Entity);
    }

    [Theory]
    [InlineData("test_wrong")]
    [InlineData("test_odd")]
    public void A_class_whose_icon_has_no_file_gets_the_plain_entity_icon(string className)
    {
        WorldIcons.ForEntity(Schemas, className).ShouldBe(WorldIcons.Entity);
    }

    [Fact]
    public void A_class_with_no_schema_gets_the_unknown_icon()
    {
        WorldIcons.ForEntity(Schemas, "from_another_game").ShouldBe(WorldIcons.UnknownEntity);
        WorldIcons.ForEntity(Schemas, null).ShouldBe(WorldIcons.UnknownEntity);
    }

    [Fact]
    public void With_no_catalogue_at_all_a_class_gets_the_plain_entity_icon()
    {
        // No catalogue says nothing about a class, so it is not called unknown.
        WorldIcons.ForEntity(null, "test_door").ShouldBe(WorldIcons.Entity);
    }

    [Fact]
    public void Both_fallback_icons_are_files()
    {
        WorldIconFiles.Has(WorldIcons.Entity).ShouldBeTrue();
        WorldIconFiles.Has(WorldIcons.UnknownEntity).ShouldBeTrue();
    }

    [Fact]
    public void Every_kind_of_light_has_a_file_of_its_own()
    {
        LightKind[] kinds = Enum.GetValues<LightKind>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (LightKind kind in kinds)
        {
            string key = WorldIcons.ForLight(kind);
            WorldIconFiles.Has(key).ShouldBeTrue($"{kind} asks for {key}");
            keys.Add(key);
        }

        keys.Count.ShouldBe(kinds.Length);
    }

    [Fact]
    public void Asking_again_for_a_class_makes_no_garbage()
    {
        Schemas.TryGetSchema("test_once", out EntitySchema? found).ShouldBeTrue();
        EntitySchema schema = found.ShouldNotBeNull();
        WorldIcons.ForClass(schema);
        WorldIcons.ForEntity(Schemas, "test_once");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            WorldIcons.ForClass(schema);
            WorldIcons.ForEntity(Schemas, "test_once");
        }

        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0);
    }
}
