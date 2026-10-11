using System;
using System.Collections.Concurrent;
using System.Text;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Icons;

/// <summary>
/// Says which icon file stands for an entity class or a kind of light. The
/// shell and the 3D view both ask here, so they show the same drawing.
/// </summary>
public static class WorldIcons
{
    /// <summary>
    /// The icon of a class that names none, or names one no file has. Every
    /// entity icon's key starts with it.
    /// </summary>
    public const string Entity = "IconEntity";

    /// <summary>The icon of a class this build has no schema for.</summary>
    public const string UnknownEntity = "IconEntityUnknown";

    // Icon name to key, so the lookup a frame makes builds no string. The
    // names come from schemas, so there are as many as there are classes.
    private static readonly ConcurrentDictionary<string, string> KeysByName = new(StringComparer.Ordinal);

    /// <summary>
    /// The key an icon name stands for: <c>IconEntity</c>, then the name in
    /// Pascal case with its hyphens gone. <c>trigger-once</c> is
    /// <c>IconEntityTriggerOnce</c>. The key may have no file.
    /// </summary>
    public static string KeyForName(string iconName)
    {
        ArgumentNullException.ThrowIfNull(iconName);

        var key = new StringBuilder(Entity, Entity.Length + iconName.Length);
        bool startsWord = true;

        foreach (char c in iconName)
        {
            if (c == '-')
            {
                startsWord = true;
                continue;
            }

            key.Append(startsWord ? char.ToUpperInvariant(c) : c);
            startsWord = false;
        }

        return key.ToString();
    }

    /// <summary>The key of the icon a class is shown with.</summary>
    public static string ForClass(EntitySchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return schema.Icon.Length == 0 ? Entity : KeysByName.GetOrAdd(schema.Icon, Resolve);
    }

    /// <summary>
    /// The key of the icon an entity of this class is shown with. A class the
    /// catalogue lacks gets <see cref="UnknownEntity"/>.
    /// </summary>
    /// <param name="schemas">
    /// The classes the session knows. Null says nothing about any class, so
    /// the answer is the plain <see cref="Entity"/>.
    /// </param>
    public static string ForEntity(EntitySchemaCatalog? schemas, string? className)
    {
        if (schemas is null)
            return Entity;

        return schemas.TryGetSchema(className, out EntitySchema? schema) ? ForClass(schema) : UnknownEntity;
    }

    /// <summary>The key of the icon a light of this kind is shown with.</summary>
    public static string ForLight(LightKind kind) => kind switch
    {
        LightKind.Directional => "IconLightSun",
        LightKind.Spot => "IconLightSpot",
        LightKind.Rect => "IconLightPanel",
        LightKind.Disc => "IconLightDisc",
        _ => "IconLight",
    };

    private static string Resolve(string iconName)
    {
        string key = KeyForName(iconName);
        return WorldIconFiles.Has(key) ? key : Entity;
    }
}
