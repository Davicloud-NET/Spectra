using Avalonia.Controls;
using SpectraEngine.Core.Entities;
using SpectraEngine.Editing.Commands;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>One entity class, as the Insert and Make entity lists show it.</summary>
// No command here: the Object menu places at the view centre and the viewport
// menu at the right-click point, so each wires its own Click.
public sealed class EntityInsertItem
{
    internal EntityInsertItem(EntitySchema schema)
    {
        ClassName = schema.ClassName;
        Display = schema.DisplayName.Length > 0 ? schema.DisplayName : schema.ClassName;
        Group = schema.Group;
        Placement = schema.Placement;
    }

    /// <summary>The wire name, as a map file spells it.</summary>
    public string ClassName { get; }

    /// <summary>The label the menu shows.</summary>
    public string Display { get; }

    /// <summary>The category the class files itself under, or empty.</summary>
    public string Group { get; }

    /// <summary>How the class is placed.</summary>
    public EntityPlacement Placement { get; }

    /// <summary>
    /// Whether the class is made from a block, a part or a group of them, so
    /// Make entity offers it.
    /// </summary>
    public bool IsMadeFromGeometry => EntityEditor.UsesGeometry(Placement);

    /// <summary>
    /// The entry's tooltip in an Insert list: the class name, its group, and
    /// what arrives when the class is made from geometry.
    /// </summary>
    public string Tip => Placement switch
    {
        EntityPlacement.Brush => $"{Identity}. Arrives as a part to move and size.",
        EntityPlacement.Volume =>
            $"{Identity}. Arrives as an outlined volume to move and size. It is not drawn or solid.",
        _ => Identity,
    };

    /// <summary>The entry's tooltip in a Make entity list.</summary>
    public string MakeTip => Placement == EntityPlacement.Volume
        ? $"{Identity}. The selection stops being drawn and solid, and shows as an outline."
        : $"{Identity}. Blocks in the selection become parts.";

    private string Identity => Group.Length > 0
        ? $"{ClassName}  ({Group})"
        : ClassName;
}

/// <summary>
/// Turns a parsed schema catalogue into the entity lists the menus show.
/// </summary>
// Reads the parsed .sentdef catalogue, not EntityCatalog.Shared, so the menu
// and the property panel describe the same classes.
public static class EntityInsertMenu
{
    /// <summary>Every class an insert can place, in catalogue order.</summary>
    /// <param name="catalog">The parsed catalogue, or null before a session exists.</param>
    public static List<EntityInsertItem> Build(EntitySchemaCatalog? catalog)
    {
        var items = new List<EntityInsertItem>();
        if (catalog is null)
            return items;

        foreach (EntitySchema schema in catalog.Schemas)
            items.Add(new EntityInsertItem(schema));

        return items;
    }

    /// <summary>
    /// The classes Make entity offers: the ones made from geometry, in the
    /// order given.
    /// </summary>
    public static List<EntityInsertItem> MadeFromGeometry(IEnumerable<EntityInsertItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var made = new List<EntityInsertItem>();
        foreach (EntityInsertItem item in items)
        {
            if (item.IsMadeFromGeometry)
                made.Add(item);
        }

        return made;
    }

    /// <summary>
    /// Rebuilds a menu's rows, one per class. Every entity list in the shell
    /// is filled here, so they read the same.
    /// </summary>
    /// <param name="forMake">Whether the rows are for Make entity rather than Insert.</param>
    /// <param name="picked">Called with the class name when a row is clicked.</param>
    public static void Fill(
        ItemCollection rows, IEnumerable<EntityInsertItem> items, bool forMake, Action<string> picked)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(picked);

        rows.Clear();

        foreach (EntityInsertItem entry in items)
        {
            // Capture the name, not the entry.
            string className = entry.ClassName;
            var row = new MenuItem { Header = entry.Display };
            ToolTip.SetTip(row, forMake ? entry.MakeTip : entry.Tip);
            row.Click += (_, _) => picked(className);
            rows.Add(row);
        }
    }
}
