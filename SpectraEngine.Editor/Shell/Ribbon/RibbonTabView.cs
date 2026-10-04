using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Shell.Ribbon;

/// <summary>
/// One tab's body: a page of the ribbon, plus the one click handler every
/// control on it goes through.
/// </summary>
// One handler for the page, resolving each control's Tag through RibbonLayout,
// so no verb lives in a click handler.
// One live instance per window, moved between the inline host and the flyout
// popup. Not a template: an instance keeps its wiring.
public abstract class RibbonTabView : UserControl
{
    /// <summary>A control on this page was clicked, carrying its verb.</summary>
    public event Action<ShellVerb>? Invoked;

    /// <summary>Which page this is. Must name a tab in <see cref="RibbonLayout.Tabs"/>.</summary>
    protected abstract string TabId { get; }

    /// <summary>
    /// The roster item a clicked control names, or null if it names none.
    /// </summary>
    // Static: the strip's undo and redo are on no page and resolve the same way.
    public static RibbonItem? ItemOf(object? sender) =>
        sender is Control { Tag: string id } ? RibbonLayout.FindItem(id) : null;

    /// <summary>Click handler for every control on the page.</summary>
    protected void OnRibbonItemClick(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
            Invoked?.Invoke(item.Verb);
    }

    /// <summary>
    /// Throws when the page's controls and its roster entry disagree. Call from
    /// the derived constructor, after the markup is built.
    /// </summary>
    protected void ValidateAgainstRoster()
    {
        RibbonTab tab = RibbonLayout.FindTab(TabId)
            ?? throw new InvalidOperationException($"'{TabId}' is not a ribbon tab.");

        IReadOnlyList<RibbonItem> items = RibbonLayout.ItemsOf(tab);
        var expected = new HashSet<string>(items.Select(i => i.Id), StringComparer.Ordinal);
        var found = new List<string>();
        var drawn = new Dictionary<string, Control>(StringComparer.Ordinal);

        foreach (ILogical logical in this.GetLogicalDescendants())
        {
            if (logical is Control { Tag: string id } control)
            {
                found.Add(id);
                drawn[id] = control;
            }
        }

        var duplicates = found.GroupBy(id => id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"The '{TabId}' ribbon page draws these ids more than once: {string.Join(", ", duplicates)}.");
        }

        var unknown = found.Where(id => !expected.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"The '{TabId}' ribbon page carries controls the roster does not know: " +
                $"{string.Join(", ", unknown)}. Add them to RibbonLayout or drop the Tag.");
        }

        var missing = expected.Where(id => !found.Contains(id, StringComparer.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The '{TabId}' ribbon page is missing controls the roster promises: " +
                $"{string.Join(", ", missing)}.");
        }

        var wrong = new List<string>();
        foreach (RibbonItem item in items)
        {
            if (!drawn.TryGetValue(item.Id, out Control? control)) continue;

            string required = RibbonLayout.RequiredClass(item);
            if (!control.Classes.Contains(required))
                wrong.Add($"{item.Id} is a {item.Kind} and must wear '{required}'");
        }

        if (wrong.Count > 0)
        {
            throw new InvalidOperationException(
                $"The '{TabId}' ribbon page draws controls the roster shapes differently: " +
                $"{string.Join("; ", wrong)}.");
        }
    }
}
