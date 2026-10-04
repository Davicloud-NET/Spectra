using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Ribbon;

/// <summary>
/// How much room a ribbon control takes. The tests compute each page's width
/// floor from it.
/// </summary>
public enum RibbonItemSize
{
    /// <summary>A 22px row: a 16px glyph and a word, three to a column.</summary>
    Small,

    /// <summary>A 64x66 button: a 32px glyph over up to two lines of label.</summary>
    Large,
}

/// <summary>
/// What a ribbon item needs wired: a lit state, a set, a typed value, two hit
/// regions. <see cref="RibbonLayout.RequiredClass"/> maps it to the style class
/// the control must wear.
/// </summary>
public enum RibbonControlKind
{
    /// <summary>Posts its verb and nothing else.</summary>
    Button,

    /// <summary>Posts a set-verb and shows whether it is the live one.</summary>
    Toggle,

    /// <summary>One of a mutually exclusive set, each a set-verb.</summary>
    Radio,

    /// <summary>An independent on/off, drawn as a box and a tick.</summary>
    Check,

    /// <summary>A two-way choice showing its current value as a word.</summary>
    Chip,

    /// <summary>Carries a typed value; commits through focus and Enter, never a click.</summary>
    Field,

    /// <summary>Half of a stepper pair beside a field.</summary>
    Stepper,

    /// <summary>A main action plus a caret that opens a list.</summary>
    Split,
}

/// <summary>One control on the ribbon.</summary>
/// <param name="Id">
/// Lower case, dotted. Also the control's <c>Tag</c> in the tab's markup; the
/// tab view checks its tree against the roster at construction.
/// </param>
/// <param name="Label">The word on the control, sentence case.</param>
/// <param name="Verb">The verb it posts.</param>
/// <param name="Size">
/// A <see cref="RibbonItemSize.Large"/> label may be at most
/// <see cref="RibbonLayout.LargeLabelLimit"/> characters.
/// </param>
/// <param name="Kind">What it needs wired.</param>
public sealed record RibbonItem(
    string Id,
    string Label,
    ShellVerb Verb,
    RibbonItemSize Size = RibbonItemSize.Small,
    RibbonControlKind Kind = RibbonControlKind.Button);

/// <summary>One captioned box of controls inside a tab.</summary>
/// <param name="Caption">Sentence case, not uppercase.</param>
public sealed record RibbonGroup(string Caption, IReadOnlyList<RibbonItem> Items);

/// <summary>One page of the command surface.</summary>
/// <param name="Id">Matches the markup file that renders it.</param>
/// <param name="Title">The word on the tab.</param>
/// <param name="Summary">What the page is for. Shown as the tab's tooltip.</param>
public sealed record RibbonTab(string Id, string Title, string Summary, IReadOnlyList<RibbonGroup> Groups);

/// <summary>
/// The ribbon's roster: which verbs are on which tab, which are never on one,
/// and which tab a session opens on.
/// </summary>
// Rules the tests hold: no verb on two tabs, Insert first on the default tab,
// and the active tab is not persisted, so every launch opens on Insert.
// No keytips or tab access keys: the menus own Alt. Ctrl+F1 collapses.
public static class RibbonLayout
{
    /// <summary>The most characters a large label can hold.</summary>
    // Measured against the 64px button. A longer single word breaks mid-word.
    public const int LargeLabelLimit = 10;

    /// <summary>The tab a session opens on, every launch.</summary>
    public const string DefaultTabId = "build";

    public const string ViewTabId = "view";

    /// <summary>
    /// Undo and redo: on the tab strip, visible expanded and collapsed, and on
    /// no tab.
    /// </summary>
    // Undo recovers from the Build tab's destructive verbs, so a collapse must
    // not hide it.
    public static IReadOnlyList<RibbonItem> AlwaysVisible { get; } =
    [
        new("history.undo", "Undo", ShellVerb.Of(EditorHostCommand.Undo)),
        new("history.redo", "Redo", ShellVerb.Of(EditorHostCommand.Redo)),
    ];

    /// <summary>The pages, in strip order.</summary>
    public static IReadOnlyList<RibbonTab> Tabs { get; } =
    [
        new(DefaultTabId, "Build",
            "Everything that changes the level: what is in it, where it is, and how it snaps.",
        [
            // Must stay the first group of the first tab.
            new RibbonGroup("Insert",
            [
                new RibbonItem("insert.block", "Block", ShellVerb.Of(InsertKind.WorldBrush),
                    RibbonItemSize.Large),
                new RibbonItem("insert.part", "Part", ShellVerb.Of(InsertKind.PartBrush),
                    RibbonItemSize.Large),
                new RibbonItem("insert.cut", "Cut", ShellVerb.Of(InsertKind.SubtractiveBrush),
                    RibbonItemSize.Large),
                new RibbonItem("insert.light", "Light", ShellVerb.Of(InsertKind.PointLight),
                    RibbonItemSize.Large),
                new RibbonItem("insert.panel", "Light panel", ShellVerb.Of(InsertKind.SurfaceLight)),
                new RibbonItem("insert.group", "Group", ShellVerb.Of(InsertKind.Group)),

                // Main half places the last class used; the caret opens the
                // list and has no Tag. The flyout's classes come from the
                // project's .sentdef at click time, so they are not roster items.
                new RibbonItem("insert.entity", "Entity", ShellVerb.InsertEntity(),
                    RibbonItemSize.Large, RibbonControlKind.Split),
            ]),

            // Two-way choices are chips: "world" is not the on state of "local".
            new RibbonGroup("Transform",
            [
                new RibbonItem("tool.move", "Move", ShellVerb.Of(GizmoCommand.UseTranslate),
                    RibbonItemSize.Large, RibbonControlKind.Toggle),
                new RibbonItem("tool.rotate", "Rotate", ShellVerb.Of(GizmoCommand.UseRotate),
                    RibbonItemSize.Large, RibbonControlKind.Toggle),
                new RibbonItem("tool.size", "Size", ShellVerb.Of(GizmoCommand.UseScale),
                    RibbonItemSize.Large, RibbonControlKind.Toggle),
                new RibbonItem("choice.axes", "Axes", ShellVerb.Of(ShellToggle.Axes),
                    RibbonItemSize.Small, RibbonControlKind.Chip),
                new RibbonItem("choice.handles", "Handles", ShellVerb.Of(ShellToggle.Handles),
                    RibbonItemSize.Small, RibbonControlKind.Chip),
            ]),

            new RibbonGroup("Snap",
            [
                new RibbonItem("choice.snap", "Snap to grid", ShellVerb.Of(ShellToggle.Snap),
                    RibbonItemSize.Small, RibbonControlKind.Check),
                new RibbonItem("snap.increment", "Increment", ShellVerb.SnapIncrement(),
                    RibbonItemSize.Small, RibbonControlKind.Field),
                new RibbonItem("snap.finer", "Finer", ShellVerb.Of(GizmoCommand.FinerSnap),
                    RibbonItemSize.Small, RibbonControlKind.Stepper),
                new RibbonItem("snap.coarser", "Coarser", ShellVerb.Of(GizmoCommand.CoarserSnap),
                    RibbonItemSize.Small, RibbonControlKind.Stepper),
            ]),

            new RibbonGroup("Arrange",
            [
                new RibbonItem("edit.duplicate", "Duplicate", ShellVerb.Of(EditorHostCommand.Duplicate),
                    RibbonItemSize.Large),
                new RibbonItem("edit.delete", "Delete", ShellVerb.Of(EditorHostCommand.Delete)),
                new RibbonItem("edit.convert", "Convert", ShellVerb.Of(EditorHostCommand.ToggleBrushKind)),
                new RibbonItem("edit.group", "Group", ShellVerb.Of(EditorHostCommand.Group)),
                new RibbonItem("edit.ungroup", "Ungroup", ShellVerb.Of(EditorHostCommand.Ungroup)),
            ]),
        ]),

        new(ViewTabId, "View",
            "Everything that changes how you look at the level, and nothing that changes the level.",
        [
            new RibbonGroup("Frame",
            [
                new RibbonItem("camera.frame", "Selection", ShellVerb.Of(EditorCameraCommand.FrameSelection),
                    RibbonItemSize.Large),
                new RibbonItem("camera.frameall", "Everything", ShellVerb.Of(EditorCameraCommand.FrameAll),
                    RibbonItemSize.Large),
            ]),

            // Radios, not a dropdown: all three modes readable at rest.
            // Exclusivity comes from the three set-verbs, not the controls.
            new RibbonGroup("Ground grid",
            [
                new RibbonItem("grid.auto", "Auto", ShellVerb.Of(EditorHostCommand.GridAuto),
                    RibbonItemSize.Small, RibbonControlKind.Radio),
                new RibbonItem("grid.on", "Always", ShellVerb.Of(EditorHostCommand.GridOn),
                    RibbonItemSize.Small, RibbonControlKind.Radio),
                new RibbonItem("grid.off", "Off", ShellVerb.Of(EditorHostCommand.GridOff),
                    RibbonItemSize.Small, RibbonControlKind.Radio),
            ]),

            new RibbonGroup("Overlays",
            [
                new RibbonItem("overlay.wireframe", "Wireframe", ShellVerb.Of(DebugVisualization.Wireframe),
                    RibbonItemSize.Small, RibbonControlKind.Check),
                new RibbonItem("overlay.vertices", "Vertices", ShellVerb.Of(DebugVisualization.Vertices),
                    RibbonItemSize.Small, RibbonControlKind.Check),
                new RibbonItem("overlay.bounds", "Bounds", ShellVerb.Of(DebugVisualization.Aabbs),
                    RibbonItemSize.Small, RibbonControlKind.Check),
                new RibbonItem("overlay.normals", "Normals", ShellVerb.Of(DebugVisualization.Normals),
                    RibbonItemSize.Small, RibbonControlKind.Check),
                new RibbonItem("overlay.axes", "Node axes", ShellVerb.Of(DebugVisualization.SceneGraph),
                    RibbonItemSize.Small, RibbonControlKind.Check),
            ]),
        ]),
    ];

    /// <summary>Every item on one tab, groups flattened, in reading order.</summary>
    public static IReadOnlyList<RibbonItem> ItemsOf(RibbonTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var items = new List<RibbonItem>();
        foreach (RibbonGroup group in tab.Groups)
            items.AddRange(group.Items);

        return items;
    }

    /// <summary>The tab with this id, or null.</summary>
    public static RibbonTab? FindTab(string? id)
    {
        if (id is null)
            return null;

        foreach (RibbonTab tab in Tabs)
        {
            if (string.Equals(tab.Id, id, StringComparison.Ordinal))
                return tab;
        }

        return null;
    }

    /// <summary>
    /// The item with this id, on a tab or the always-visible strip, or null.
    /// </summary>
    public static RibbonItem? FindItem(string? id)
    {
        if (id is null)
            return null;

        foreach (RibbonItem item in AlwaysVisible)
        {
            if (string.Equals(item.Id, id, StringComparison.Ordinal))
                return item;
        }

        foreach (RibbonTab tab in Tabs)
        {
            foreach (RibbonGroup group in tab.Groups)
            {
                foreach (RibbonItem item in group.Items)
                {
                    if (string.Equals(item.Id, id, StringComparison.Ordinal))
                        return item;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The style class a control must wear to realize its declared
    /// <see cref="RibbonControlKind"/>, given its <see cref="RibbonItemSize"/>.
    /// </summary>
    // Read by both the page's runtime validator and the tests that scan the
    // markup. Without it a check row drawn as a plain button passes the Tag
    // check and never lights.
    public static string RequiredClass(RibbonItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Kind switch
        {
            RibbonControlKind.Check => "rcheck",

            // No geometry of its own; exists so the validators can tell a
            // toggle from a plain button.
            RibbonControlKind.Toggle => "rtoggle",
            RibbonControlKind.Radio => "rradio",
            RibbonControlKind.Chip => "chip",
            RibbonControlKind.Field => "field",
            RibbonControlKind.Stepper => "rspin",

            // Only the split's main half is tagged, and it is a large button.
            RibbonControlKind.Split => "rbig",

            _ => item.Size == RibbonItemSize.Large ? "rbig" : "rsmall",
        };
    }
}
