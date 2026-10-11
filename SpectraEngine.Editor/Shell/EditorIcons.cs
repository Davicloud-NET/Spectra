using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Says what icon and what tint a thing has: a node, an entity class, a kind
/// of light, a file. Every surface of the shell asks here, so one thing has
/// one drawing wherever it shows.
/// </summary>
// The only place in the shell that names the icon of an entity, a light or a
// file: a test reads the sources and fails on such a key anywhere else. The
// same goes for an entity's or a light's tint, but for the theme's kind-entity
// and kind-light style classes, which a test holds to the tints here. Which
// file a class or a light gets is SpectraEngine.Editing's rule, and its 3D
// view follows the same one.
public static class EditorIcons
{
    private const string MutedTint = "SpectraTextMuted";
    private const string EntityTint = "SpectraKindEntity";
    private const string LightTint = "SpectraKindLight";

    /// <summary>What a group's tint token starts with. The group's name follows.</summary>
    public const string GroupTintPrefix = "SpectraGroup";

    // A large ribbon button's 24 box against the 16 box the icon files are on.
    private const double LargeScale = 24.0 / 16.0;

    private static readonly Dictionary<string, string> GroupTints = BuildGroupTints();

    // The file a sound entity plays looks as that entity does.
    private static readonly IconLook SoundFile = new(WorldIcons.KeyForName("sound"), GroupTint("Sound"));

    // A mesh node, and the model file it is made from.
    private static readonly IconLook Mesh = new("IconMesh", "SpectraKindMesh");

    /// <summary>An entity of no particular class, as a menu names the kind.</summary>
    public static IconLook PlainEntity { get; } = new(WorldIcons.Entity, EntityTint);

    /// <summary>A light of no particular kind, as a menu names the kind.</summary>
    public static IconLook PlainLight { get; } = ForLight(LightKind.Point);

    /// <summary>
    /// Whoever set a wire off, on a Logic card: an entity nobody can name
    /// ahead of time, so it is the plain entity said quietly.
    /// </summary>
    public static IconLook Activator { get; } = PlainEntity with { Tint = MutedTint };

    /// <summary>For markup: <see cref="PlainEntity"/>'s glyph.</summary>
    public static Geometry? PlainEntityGlyph => Glyph(PlainEntity);

    /// <summary>For markup: <see cref="PlainLight"/>'s glyph.</summary>
    public static Geometry? PlainLightGlyph => Glyph(PlainLight);

    /// <summary>For markup: <see cref="PlainEntity"/>'s glyph on a large button.</summary>
    public static Geometry? PlainEntityLargeGlyph => LargeGlyph(PlainEntity);

    /// <summary>For markup: <see cref="PlainLight"/>'s glyph on a large button.</summary>
    public static Geometry? PlainLightLargeGlyph => LargeGlyph(PlainLight);

    /// <summary>For markup: the glyph of the light a surface light is, a panel.</summary>
    public static Geometry? SurfaceLightGlyph => Glyph(ForLight(LightKind.Rect));

    /// <summary>
    /// The look of a node in the scene. An entity shows its class, whatever
    /// else the node carries, and a light shows its kind.
    /// </summary>
    /// <param name="entityClass">
    /// The node's entity class, or null when it carries no entity. Empty is an
    /// entity that has no class yet.
    /// </param>
    /// <param name="light">The node's kind of light, or null when it carries none.</param>
    /// <param name="schemas">The classes the session knows, or null with no session.</param>
    public static IconLook ForNode(
        SceneNodeKind kind, string? entityClass, LightKind? light, EntitySchemaCatalog? schemas)
    {
        if (entityClass is not null)
            return ForEntity(schemas, entityClass);

        return kind switch
        {
            SceneNodeKind.Group => new IconLook("IconGroup", "SpectraKindGroup"),
            SceneNodeKind.Mesh => Mesh,
            SceneNodeKind.BrushWorld => new IconLook("IconBrushWorld", "SpectraKindBrushWorld"),
            SceneNodeKind.BrushPart => new IconLook("IconBrushPart", "SpectraKindBrushPart"),
            SceneNodeKind.BrushSubtractive => new IconLook("IconBrushSubtractive", "SpectraKindBrushSubtractive"),
            SceneNodeKind.Light => light is { } lightKind ? ForLight(lightKind) : PlainLight,
            SceneNodeKind.Entity => PlainEntity,
            _ => new IconLook("IconEmpty", MutedTint),
        };
    }

    /// <summary>
    /// The look of an entity of this class: the icon its class names, in its
    /// group's tint. A class the catalogue lacks gets the unknown icon.
    /// </summary>
    public static IconLook ForEntity(EntitySchemaCatalog? schemas, string? className)
    {
        EntitySchema? schema = null;
        schemas?.TryGetSchema(className, out schema);
        return new IconLook(WorldIcons.ForEntity(schemas, className), GroupTint(schema?.Group));
    }

    /// <summary>The look of an entity class, for a menu or a card.</summary>
    public static IconLook ForEntityClass(EntitySchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new IconLook(WorldIcons.ForClass(schema), GroupTint(schema.Group));
    }

    /// <summary>The look of a light of this kind.</summary>
    public static IconLook ForLight(LightKind kind) => new(WorldIcons.ForLight(kind), LightTint);

    /// <summary>The look of a file in the Content panel.</summary>
    public static IconLook ForFile(ContentKind kind) => kind switch
    {
        ContentKind.Folder => new IconLook("IconOpenFolder", "SpectraMode"),
        ContentKind.Texture => new IconLook("IconFileTexture", "SpectraKindBrushWorld"),
        ContentKind.Material => new IconLook("IconFileMaterial", "SpectraKindBrushPart"),
        ContentKind.Model => Mesh,
        ContentKind.Shader => new IconLook("IconFileShader", LightTint),
        ContentKind.Sound => SoundFile,
        _ => new IconLook("IconEmpty", MutedTint),
    };

    /// <summary>
    /// The tint token of an entity group. A group with no token of its own
    /// gets the plain entity tint.
    /// </summary>
    public static string GroupTint(string? group) =>
        group is not null && GroupTints.TryGetValue(group, out string? key) ? key : EntityTint;

    /// <summary>The look's glyph from the theme, or null when the theme lacks it.</summary>
    public static Geometry? Glyph(IconLook look) => Find<Geometry>(look.Icon);

    /// <summary>
    /// The look's glyph for the ribbon's large buttons, or null when the theme
    /// lacks it. It is the small glyph scaled up, so a large button cannot
    /// show another drawing.
    /// </summary>
    // A Path draws it with Stretch None. Avalonia throws when a stretched
    // Path draws a geometry that carries a transform.
    public static Geometry? LargeGlyph(IconLook look)
    {
        // The build writes every icon as a PathGeometry.
        if (Glyph(look) is not PathGeometry small)
            return null;

        Matrix placed = small.Transform?.Value ?? Matrix.Identity;

        return new PathGeometry
        {
            Figures = small.Figures,
            FillRule = small.FillRule,
            Transform = new MatrixTransform(placed * Matrix.CreateScale(LargeScale, LargeScale)),
        };
    }

    /// <summary>The look's tint from the theme, or null when the theme lacks it.</summary>
    public static IBrush? Brush(IconLook look) => Find<IBrush>(look.Tint);

    private static T? Find<T>(string key) where T : class =>
        Application.Current?.TryFindResource(key, out object? value) == true ? value as T : null;

    // The 3D view's table of group colours names the groups. The theme has a
    // token for each, and a test holds the two to the same values.
    private static Dictionary<string, string> BuildGroupTints()
    {
        var tints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string group, _) in EntityGroupTints.Table)
            tints.Add(group, GroupTintPrefix + group);

        return tints;
    }
}
