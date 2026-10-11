using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The one place the shell asks what icon and what tint a thing has. Every
/// answer has to be a glyph that is a file and a brush the theme defines.
/// </summary>
public sealed class EditorIconsTests
{
    private static readonly HashSet<string> Icons = IconFiles.Keys();
    private static readonly Dictionary<string, string> Brushes = IconFiles.ThemeBrushes();

    // A class from another game, one whose icon no file has, one whose group
    // has no tint, and one that states nothing.
    private static readonly EntitySchemaCatalog Odd = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema("test_wrong", group: "Movers", icon: "no-such-drawing"),
        new EntitySchema("test_other_group", group: "Gameplay", icon: "door"),
        new EntitySchema("test_bare"),
    ]));

    public static TheoryData<SceneNodeKind> EveryNodeKind => new(Enum.GetValues<SceneNodeKind>());

    public static TheoryData<LightKind> EveryLightKind => new(Enum.GetValues<LightKind>());

    public static TheoryData<ContentKind> EveryFileKind => new(Enum.GetValues<ContentKind>());

    private static void ShouldExist(IconLook look, string what)
    {
        Icons.ShouldContain(look.Icon, $"{what} asks for an icon with no file");
        Brushes.ShouldContainKey(look.Tint, $"{what} asks for a tint the theme does not define");
    }

    [Theory]
    [MemberData(nameof(EveryNodeKind))]
    public void Every_kind_of_node_has_an_icon_that_exists_and_a_tint_that_exists(SceneNodeKind kind)
    {
        ShouldExist(EditorIcons.ForNode(kind, null, null, BuiltinClasses.Catalog), kind.ToString());
        ShouldExist(EditorIcons.ForNode(kind, null, null, null), $"{kind} with no session");
    }

    [Fact]
    public void Every_kind_of_node_has_an_icon_of_its_own()
    {
        SceneNodeKind[] kinds = Enum.GetValues<SceneNodeKind>();

        kinds.Select(kind => EditorIcons.ForNode(kind, null, null, null).Icon)
            .Distinct()
            .Count()
            .ShouldBe(kinds.Length);
    }

    [Fact]
    public void Every_built_in_class_has_its_own_icon_in_its_groups_tint()
    {
        BuiltinClasses.Catalog.Count.ShouldBeGreaterThan(10);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (EntitySchema schema in BuiltinClasses.Catalog.Schemas)
        {
            IconLook look = EditorIcons.ForEntity(BuiltinClasses.Catalog, schema.ClassName);

            ShouldExist(look, schema.ClassName);
            look.ShouldBe(EditorIcons.ForEntityClass(schema));
            look.Icon.ShouldBe(WorldIcons.KeyForName(schema.Icon), schema.ClassName);
            look.Tint.ShouldBe(EditorIcons.GroupTintPrefix + schema.Group, schema.ClassName);
            seen.Add(look.Icon).ShouldBeTrue($"{schema.ClassName} shares {look.Icon} with another class");
        }
    }

    [Fact]
    public void A_class_from_another_game_gets_the_unknown_icon_in_the_plain_entity_tint()
    {
        IconLook look = EditorIcons.ForEntity(BuiltinClasses.Catalog, "from_another_game");

        look.Icon.ShouldBe(WorldIcons.UnknownEntity);
        look.Tint.ShouldBe(EditorIcons.PlainEntity.Tint);
        ShouldExist(look, "a placeholder");
    }

    [Fact]
    public void A_class_whose_icon_no_file_has_gets_the_plain_entity_icon_in_its_groups_tint()
    {
        IconLook look = EditorIcons.ForEntity(Odd, "test_wrong");

        look.Icon.ShouldBe(WorldIcons.Entity);
        look.Tint.ShouldBe(EditorIcons.GroupTint("Movers"));
        ShouldExist(look, "a class with a wrong icon name");
    }

    [Fact]
    public void A_class_that_states_nothing_gets_the_plain_entity_look()
    {
        EditorIcons.ForEntity(Odd, "test_bare").ShouldBe(EditorIcons.PlainEntity);
        ShouldExist(EditorIcons.PlainEntity, "the plain entity");
    }

    [Fact]
    public void A_group_with_no_tint_of_its_own_gets_the_plain_entity_tint()
    {
        IconLook look = EditorIcons.ForEntity(Odd, "test_other_group");

        look.Icon.ShouldBe(WorldIcons.KeyForName("door"));
        look.Tint.ShouldBe(EditorIcons.PlainEntity.Tint);
        EditorIcons.GroupTint(null).ShouldBe(EditorIcons.PlainEntity.Tint);
        EditorIcons.GroupTint("").ShouldBe(EditorIcons.PlainEntity.Tint);
    }

    [Fact]
    public void With_no_session_an_entity_is_the_plain_entity_and_not_called_unknown()
    {
        EditorIcons.ForEntity(null, "func_door").ShouldBe(EditorIcons.PlainEntity);
    }

    [Theory]
    [InlineData(SceneNodeKind.BrushPart)]
    [InlineData(SceneNodeKind.BrushWorld)]
    [InlineData(SceneNodeKind.Entity)]
    [InlineData(SceneNodeKind.Light)]
    public void A_node_that_carries_an_entity_shows_its_class_whatever_else_it_carries(SceneNodeKind kind)
    {
        // A door is a part to the engine, and a door to whoever reads the tree.
        EditorIcons.ForNode(kind, "func_door", LightKind.Spot, BuiltinClasses.Catalog)
            .ShouldBe(EditorIcons.ForEntity(BuiltinClasses.Catalog, "func_door"));
    }

    [Fact]
    public void An_entity_with_no_class_yet_gets_the_icon_the_3d_view_and_a_logic_card_give_it()
    {
        IconLook look = EditorIcons.ForNode(SceneNodeKind.Entity, "", null, BuiltinClasses.Catalog);

        look.Icon.ShouldBe(WorldIcons.ForEntity(BuiltinClasses.Catalog, ""));
        look.ShouldBe(EditorIcons.ForEntity(BuiltinClasses.Catalog, ""));
        look.Icon.ShouldBe(WorldIcons.UnknownEntity);
    }

    [Fact]
    public void A_plain_part_block_or_cut_keeps_its_own_icon()
    {
        EditorIcons.ForNode(SceneNodeKind.BrushPart, null, null, BuiltinClasses.Catalog).Icon.ShouldBe("IconBrushPart");
        EditorIcons.ForNode(SceneNodeKind.BrushWorld, null, null, BuiltinClasses.Catalog).Icon.ShouldBe("IconBrushWorld");
        EditorIcons.ForNode(SceneNodeKind.BrushSubtractive, null, null, BuiltinClasses.Catalog).Icon
            .ShouldBe("IconBrushSubtractive");
    }

    [Theory]
    [MemberData(nameof(EveryLightKind))]
    public void Every_kind_of_light_has_an_icon_that_exists_in_the_light_tint(LightKind kind)
    {
        IconLook look = EditorIcons.ForLight(kind);

        ShouldExist(look, kind.ToString());
        look.Icon.ShouldBe(WorldIcons.ForLight(kind));
        look.Tint.ShouldBe(EditorIcons.PlainLight.Tint);
        EditorIcons.ForNode(SceneNodeKind.Light, null, kind, null).ShouldBe(look);
    }

    [Fact]
    public void Every_kind_of_light_has_an_icon_of_its_own()
    {
        LightKind[] kinds = Enum.GetValues<LightKind>();

        kinds.Select(kind => EditorIcons.ForLight(kind).Icon).Distinct().Count().ShouldBe(kinds.Length);
    }

    [Theory]
    [MemberData(nameof(EveryFileKind))]
    public void Every_kind_of_file_has_an_icon_that_exists_and_a_tint_that_exists(ContentKind kind)
    {
        ShouldExist(EditorIcons.ForFile(kind), kind.ToString());
    }

    [Fact]
    public void No_two_kinds_of_file_share_an_icon()
    {
        ContentKind[] kinds = Enum.GetValues<ContentKind>();

        kinds.Select(kind => EditorIcons.ForFile(kind).Icon).Distinct().Count().ShouldBe(kinds.Length);
    }

    [Fact]
    public void A_sound_file_looks_as_the_sound_entity_does()
    {
        EditorIcons.ForFile(ContentKind.Sound).ShouldBe(EditorIcons.ForEntity(BuiltinClasses.Catalog, "point_sound"));
    }

    [Fact]
    public void The_activator_is_the_plain_entity_in_a_quieter_tint()
    {
        EditorIcons.Activator.Icon.ShouldBe(EditorIcons.PlainEntity.Icon);
        EditorIcons.Activator.Tint.ShouldNotBe(EditorIcons.PlainEntity.Tint);
        ShouldExist(EditorIcons.Activator, "the activator");
    }

    [Fact]
    public void A_model_file_looks_as_the_mesh_node_it_becomes_does()
    {
        EditorIcons.ForFile(ContentKind.Model).ShouldBe(EditorIcons.ForNode(SceneNodeKind.Mesh, null, null, null));
    }

    [Fact]
    public void Asking_for_a_look_makes_no_garbage()
    {
        EditorIcons.ForNode(SceneNodeKind.BrushPart, "func_door", null, BuiltinClasses.Catalog);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            EditorIcons.ForNode(SceneNodeKind.BrushPart, "func_door", null, BuiltinClasses.Catalog);
            EditorIcons.ForNode(SceneNodeKind.Light, null, LightKind.Spot, BuiltinClasses.Catalog);
            EditorIcons.ForFile(ContentKind.Sound);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0);
    }
}
