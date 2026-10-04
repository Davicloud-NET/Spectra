using SpectraEngine.Core.Entities;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>One entry of the Insert menu's entity submenu.</summary>
// No command here: the Object menu places at the view centre and the viewport
// menu at the right-click point, so each wires its own Click.
public sealed class EntityInsertItem
{
    internal EntityInsertItem(EntitySchema schema)
    {
        ClassName = schema.ClassName;
        Display = schema.DisplayName.Length > 0 ? schema.DisplayName : schema.ClassName;
        Group = schema.Group;
    }

    /// <summary>The wire name, as a map file spells it.</summary>
    public string ClassName { get; }

    /// <summary>The label the menu shows.</summary>
    public string Display { get; }

    /// <summary>The category the class files itself under, or empty.</summary>
    public string Group { get; }

    /// <summary>The entry's tooltip: the class name and its group.</summary>
    public string Tip => Group.Length > 0
        ? $"{ClassName}  ({Group})"
        : ClassName;
}

/// <summary>
/// Turns a parsed schema catalogue into the Insert menu's entity entries.
/// </summary>
// Reads the parsed .sentdef catalogue, not EntityCatalog.Shared, so the menu
// and the property panel describe the same classes.
public static class EntityInsertMenu
{
    /// <summary>
    /// The classes a point insert can place, in catalogue order.
    /// </summary>
    /// <param name="catalog">The parsed catalogue, or null before a session exists.</param>
    public static List<EntityInsertItem> Build(EntitySchemaCatalog? catalog)
    {
        var items = new List<EntityInsertItem>();
        if (catalog is null)
            return items;

        foreach (EntitySchema schema in catalog.Schemas)
        {
            // A brush class needs geometry a point insert does not create.
            if (schema.Placement == EntityPlacement.Brush)
                continue;

            items.Add(new EntityInsertItem(schema));
        }

        return items;
    }
}
