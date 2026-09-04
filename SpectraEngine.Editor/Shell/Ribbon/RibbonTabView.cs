using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Shell.Ribbon;

/// <summary>
/// One tab's body: the markup that draws a page of the ribbon, plus the one
/// click handler every control on it goes through.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE handler for the whole page, resolving the control's own <c>Tag</c>
/// through <see cref="RibbonLayout"/>.</b> A handler per button would put a
/// verb in a click handler, which is exactly what <c>ROADMAP.md</c>'s ribbon
/// bullet says not to let happen ("do not let a control grow logic that lives
/// only in its click handler"), and it would make the roster a description of
/// the markup rather than the thing the markup is checked against.
/// </para>
/// <para>
/// <b>The page validates itself against the roster at construction</b>, so a
/// button whose <c>Tag</c> the roster does not know, or a roster entry with no
/// control, is a refusal at window construction rather than a control that
/// silently does nothing. That is the runtime half; <c>RibbonLayoutTests</c> is
/// the CI half, which reads the same fact out of the sources without needing an
/// Avalonia application.
/// </para>
/// <para>
/// <b>A body is re-parented between the inline host and the flyout popup</b>, so
/// it is one live instance for the window's life and never a template - the
/// same rule as <c>SetToolContent</c>, and for the same reason: an instance
/// keeps its wiring, a template rebuilds it.
/// </para>
/// </remarks>
public abstract class RibbonTabView : UserControl
{
    /// <summary>A control on this page was clicked, carrying its verb.</summary>
    public event Action<ShellVerb>? Invoked;

    /// <summary>Which page this is. Must name a tab in <see cref="RibbonLayout.Tabs"/>.</summary>
    protected abstract string TabId { get; }

    /// <summary>
    /// The roster item a clicked control names, or null if it names none.
    /// </summary>
    /// <remarks>
    /// Public and static because the strip's own controls are not on a page and
    /// still have to resolve the same way. Undo and redo used to call their
    /// handlers directly and carry a <c>Tag</c> only so a test could find them,
    /// which meant <see cref="RibbonLayout.AlwaysVisible"/>'s verbs were never
    /// dispatched at all: changing either one left the buttons still doing undo
    /// and redo with every test green. That is a verb living in a click handler,
    /// which is the one thing this roster exists to refuse.
    /// </remarks>
    public static RibbonItem? ItemOf(object? sender) =>
        sender is Control { Tag: string id } ? RibbonLayout.FindItem(id) : null;

    /// <summary>
    /// Every control on this page routes here. The <c>Tag</c> is the roster id.
    /// </summary>
    protected void OnRibbonItemClick(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
            Invoked?.Invoke(item.Verb);
    }

    /// <summary>
    /// Refuses a page whose controls and whose roster entry disagree.
    /// </summary>
    /// <remarks>
    /// Called from the derived control's constructor, after its markup is
    /// built. Both directions matter and they fail differently: a tagged
    /// control the roster has never heard of is a button that does nothing, and
    /// a roster entry with no control is a verb the tests believe is on screen
    /// and is not - which would let the no-duplicate guard pass over a surface
    /// that no longer exists.
    /// </remarks>
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

        // And the control must be the SHAPE the roster declares. Without this
        // a page could draw anything it liked under a valid Tag: a check row
        // rendered as a plain button looks finished, posts the right verb and
        // has no lit state at all, which is a control that lies about what the
        // engine is doing.
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
