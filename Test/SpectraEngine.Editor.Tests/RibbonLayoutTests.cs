using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The ribbon's roster: no verb on two tabs, Insert on the first page, every
/// page worth switching to, and the markup matching the roster.
/// </summary>
public sealed class RibbonLayoutTests
{
    [Fact]
    public void No_verb_appears_on_more_than_one_tab()
    {
        var seen = new Dictionary<ShellVerb, string>();
        var offenders = new List<string>();

        foreach (RibbonTab tab in RibbonLayout.Tabs)
        {
            foreach (RibbonItem item in RibbonLayout.ItemsOf(tab))
            {
                if (seen.TryGetValue(item.Verb, out string? firstTab))
                {
                    if (!string.Equals(firstTab, tab.Id, StringComparison.Ordinal))
                        offenders.Add($"{item.Verb.Kind}:{item.Id} is on both '{firstTab}' and '{tab.Id}'");
                }
                else
                {
                    seen[item.Verb] = tab.Id;
                }
            }
        }

        offenders.ShouldBeEmpty(
            "a verb on two tabs is what made the previous tab strip's pages interchangeable, " +
            "which taught users within one session that switching does nothing");
    }

    [Fact]
    public void A_verb_that_must_always_be_reachable_is_on_no_tab()
    {
        // A collapsed ribbon hides every page, and undo must stay reachable.
        var onTabs = new HashSet<ShellVerb>(
            RibbonLayout.Tabs.SelectMany(RibbonLayout.ItemsOf).Select(i => i.Verb));

        foreach (RibbonItem item in RibbonLayout.AlwaysVisible)
        {
            onTabs.ShouldNotContain(
                item.Verb,
                $"'{item.Id}' is always visible on the tab strip and must not also be on a page");
        }
    }

    [Fact]
    public void Undo_and_redo_are_the_always_visible_pair()
    {
        RibbonLayout.AlwaysVisible.Select(i => i.Verb).ShouldBe(
        [
            ShellVerb.Of(EditorHostCommand.Undo),
            ShellVerb.Of(EditorHostCommand.Redo),
        ]);
    }

    [Fact]
    public void Every_item_id_is_unique_across_the_whole_roster()
    {
        // The click handler resolves a control by id.
        List<string> ids =
        [
            .. RibbonLayout.AlwaysVisible.Select(i => i.Id),
            .. RibbonLayout.Tabs.SelectMany(RibbonLayout.ItemsOf).Select(i => i.Id),
        ];

        ids.GroupBy(id => id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Insert_is_the_first_group_of_the_tab_a_session_opens_on()
    {
        RibbonTab? start = RibbonLayout.FindTab(RibbonLayout.DefaultTabId);
        start.ShouldNotBeNull();

        start.Groups[0].Caption.ShouldBe(
            "Insert",
            "the previous strip put the one thing a first session needs on the tab nobody opened");

        foreach (RibbonTab tab in RibbonLayout.Tabs)
        {
            foreach (RibbonItem item in RibbonLayout.ItemsOf(tab))
            {
                if (item.Verb.Kind != ShellVerbKind.Insert)
                    continue;

                tab.Id.ShouldBe(RibbonLayout.DefaultTabId, $"'{item.Id}' is an insert");
            }
        }
    }

    [Fact]
    public void A_session_opens_on_the_default_tab_however_the_last_one_ended()
    {
        // The pin persists, the page does not: reopening on View would hide Insert.
        RibbonSurface.Create(expanded: true).ActiveTabId.ShouldBe(RibbonLayout.DefaultTabId);
        RibbonSurface.Create(expanded: false).ActiveTabId.ShouldBe(RibbonLayout.DefaultTabId);
    }

    [Fact]
    public void Every_tab_carries_enough_to_be_worth_switching_to()
    {
        // Floor: eight controls in three groups. Less than that fits on one row
        // and does not need a tab.
        foreach (RibbonTab tab in RibbonLayout.Tabs)
        {
            IReadOnlyList<RibbonItem> items = RibbonLayout.ItemsOf(tab);

            items.Count.ShouldBeGreaterThanOrEqualTo(8, $"'{tab.Id}' is too thin to be a page");
            tab.Groups.Count.ShouldBeGreaterThanOrEqualTo(3, $"'{tab.Id}' is one cluster, not a page");

            foreach (RibbonGroup group in tab.Groups)
                group.Items.ShouldNotBeEmpty($"'{tab.Id}' has an empty group named {group.Caption}");
        }
    }

    [Fact]
    public void Two_tabs_is_the_count_and_a_third_has_to_argue_for_itself()
    {
        // Pinned on purpose. A third tab changes this line and says why.
        RibbonLayout.Tabs.Count.ShouldBe(2);
        RibbonLayout.Tabs.Select(t => t.Id).ShouldBe([RibbonLayout.DefaultTabId, RibbonLayout.ViewTabId]);
    }

    [Fact]
    public void The_two_pages_divide_on_a_real_axis()
    {
        // Build changes the level, View changes how it is looked at.
        RibbonTab view = RibbonLayout.FindTab(RibbonLayout.ViewTabId)!;
        RibbonTab build = RibbonLayout.FindTab(RibbonLayout.DefaultTabId)!;

        foreach (RibbonItem item in RibbonLayout.ItemsOf(view))
        {
            item.Verb.Kind.ShouldNotBe(ShellVerbKind.Insert, item.Id);
            if (item.Verb.Kind == ShellVerbKind.Host)
            {
                item.Verb.Host.ShouldBeOneOf(
                    EditorHostCommand.GridAuto, EditorHostCommand.GridOn, EditorHostCommand.GridOff);
            }
        }

        foreach (RibbonItem item in RibbonLayout.ItemsOf(build))
        {
            item.Verb.Kind.ShouldNotBe(ShellVerbKind.Camera, item.Id);
            item.Verb.Kind.ShouldNotBe(ShellVerbKind.Debug, item.Id);
        }
    }

    [Fact]
    public void Every_ribbon_control_names_a_verb_the_editor_already_has()
    {
        foreach (RibbonItem item in AllItems())
        {
            item.Id.ShouldNotBeNullOrWhiteSpace();
            item.Label.ShouldNotBeNullOrWhiteSpace();
            item.Verb.Kind.ShouldNotBe(ShellVerbKind.None, item.Id);

            switch (item.Verb.Kind)
            {
                case ShellVerbKind.Host:
                    Enum.IsDefined(item.Verb.Host).ShouldBeTrue(item.Id);
                    break;

                case ShellVerbKind.Gizmo:
                    Enum.IsDefined(item.Verb.Gizmo).ShouldBeTrue(item.Id);
                    break;

                case ShellVerbKind.Camera:
                    Enum.IsDefined(item.Verb.Camera).ShouldBeTrue(item.Id);
                    break;

                case ShellVerbKind.Insert:
                    Enum.IsDefined(item.Verb.Insert).ShouldBeTrue(item.Id);
                    break;

                case ShellVerbKind.Debug:
                    // One flag, not a combination.
                    Enum.IsDefined(item.Verb.Debug).ShouldBeTrue(item.Id);
                    item.Verb.Debug.ShouldNotBe(DebugVisualization.None, item.Id);
                    break;

                case ShellVerbKind.Toggle:
                    Enum.IsDefined(item.Verb.Toggle).ShouldBeTrue(item.Id);

                    ShellToggles.CommandFor(item.Verb.Toggle, on: true)
                        .ShouldNotBe(ShellToggles.CommandFor(item.Verb.Toggle, on: false), item.Id);
                    break;

                case ShellVerbKind.Document:
                case ShellVerbKind.Play:
                case ShellVerbKind.Panel:
                case ShellVerbKind.Ribbon:
                case ShellVerbKind.Workspace:
                    // Shell verbs resolve to window handlers and stay off the ribbon.
                    throw new Xunit.Sdk.XunitException(
                        $"{item.Id} carries a shell verb; those belong to the menus and the palette");
            }
        }
    }

    [Fact]
    public void Every_tab_says_what_it_is_for()
    {
        foreach (RibbonTab tab in RibbonLayout.Tabs)
        {
            tab.Summary.ShouldNotBeNullOrWhiteSpace($"{tab.Id} should say what it is for");
            tab.Summary.Length.ShouldBeGreaterThan(20, $"{tab.Id}'s summary should be a sentence");
        }

        RibbonLayout.Tabs.Select(t => t.Summary).Distinct().Count()
            .ShouldBe(RibbonLayout.Tabs.Count, "two tabs that describe themselves the same way divide on nothing");
    }

    [Fact]
    public void The_ribbon_can_be_collapsed_from_the_keyboard()
    {
        // No KeyTips (Alt belongs to the menus), so this is the only keyboard route.
        string window = File.ReadAllText(
            Path.Combine(SourceRoot(), "SpectraEngine.Editor", "MainWindow.axaml.cs"));

        window.ShouldContain(
            "AddChord(Key.F1, KeyModifiers.Control",
            customMessage: "Ctrl+F1 is Office's chord for collapsing a ribbon and the only one this surface has");
    }

    [Fact]
    public void The_always_visible_pair_dispatches_through_the_roster()
    {
        // A Click wired straight to a handler would ignore the roster's verb.
        string window = File.ReadAllText(
            Path.Combine(SourceRoot(), "SpectraEngine.Editor", "MainWindow.axaml"));

        foreach (RibbonItem item in RibbonLayout.AlwaysVisible)
        {
            ElementFor(window, item.Id).ShouldContain(
                "Click=\"OnRibbonStripClick\"",
                customMessage: $"{item.Id} must resolve its verb through the roster, not name a handler");
        }
    }

    [Fact]
    public void A_two_way_choice_posts_the_verb_for_the_state_it_is_ENTERING()
    {
        // "On" is the non-default half: local axes, Classic handles, snapping.
        ShellToggles.CommandFor(ShellToggle.Axes, on: true).ShouldBe(GizmoCommand.UseLocalOrientation);
        ShellToggles.CommandFor(ShellToggle.Axes, on: false).ShouldBe(GizmoCommand.UseWorldOrientation);
        ShellToggles.CommandFor(ShellToggle.Handles, on: true).ShouldBe(GizmoCommand.UseClassicStyle);
        ShellToggles.CommandFor(ShellToggle.Handles, on: false).ShouldBe(GizmoCommand.UseStudioStyle);
        ShellToggles.CommandFor(ShellToggle.Snap, on: true).ShouldBe(GizmoCommand.EnableSnap);
        ShellToggles.CommandFor(ShellToggle.Snap, on: false).ShouldBe(GizmoCommand.DisableSnap);
    }

    [Fact]
    public void A_two_way_choice_is_never_resolved_in_a_click_handler()
    {
        // Only these six. The window may still name FinerSnap, CoarserSnap and the tool verbs.
        string window = File.ReadAllText(
            Path.Combine(SourceRoot(), "SpectraEngine.Editor", "MainWindow.axaml.cs"));

        foreach (string verb in new[]
                 {
                     nameof(GizmoCommand.UseWorldOrientation), nameof(GizmoCommand.UseLocalOrientation),
                     nameof(GizmoCommand.UseStudioStyle), nameof(GizmoCommand.UseClassicStyle),
                     nameof(GizmoCommand.EnableSnap), nameof(GizmoCommand.DisableSnap),
                 })
        {
            window.ShouldNotContain(
                verb,
                customMessage: $"{verb} belongs to ShellToggles.CommandFor; a handler naming it is the " +
                               "second expression of the pairing that made the first one dead");
        }
    }

    [Fact]
    public void There_is_exactly_one_snap_increment_field()
    {
        AllItems().Count(i => i.Verb.Kind == ShellVerbKind.SnapIncrement).ShouldBe(1);
    }

    [Fact]
    public void There_is_exactly_one_split_button_and_it_places_an_entity()
    {
        List<RibbonItem> splits = AllItems()
            .Where(i => i.Kind == RibbonControlKind.Split)
            .ToList();

        splits.Count.ShouldBe(1);
        splits[0].Verb.Kind.ShouldBe(ShellVerbKind.InsertEntity);
        splits[0].Size.ShouldBe(RibbonItemSize.Large);
    }

    [Fact]
    public void The_entity_class_list_is_not_in_the_roster()
    {
        // Entity classes come from the project's .sentdef at runtime, so the
        // roster names the control only. One payload-free verb, no class names.
        AllItems()
            .Count(i => i.Verb.Kind == ShellVerbKind.InsertEntity)
            .ShouldBe(1, "the class is session state, so only the control is in the roster");

        foreach (string className in new[] { "logic_relay", "logic_timer", "math_counter" })
        {
            AllItems()
                .Any(i => i.Id.Contains(className, StringComparison.Ordinal)
                       || i.Label.Contains(className, StringComparison.Ordinal))
                .ShouldBeFalse($"'{className}' is a project's data, not this build's roster");
        }
    }

    [Fact]
    public void The_splits_caret_carries_no_tag_of_its_own()
    {
        // The caret opens a list and posts no verb. The page validator only
        // looks at tagged controls, which lets the split be two Buttons under one id.
        string markup = File.ReadAllText(Path.Combine(RibbonFolder(), "RibbonBuildTab.axaml"));

        int splitStart = markup.IndexOf("Classes=\"rsplit\"", StringComparison.Ordinal);
        splitStart.ShouldBeGreaterThan(-1, "the split button should be drawn");

        int splitEnd = markup.IndexOf("</StackPanel>", splitStart, StringComparison.Ordinal);
        string split = markup[splitStart..splitEnd];

        Regex.Matches(split, "Tag=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ShouldBe(["insert.entity"], "only the main half names a verb");
    }

    [Fact]
    public void Finding_an_item_by_its_id_is_how_a_click_resolves()
    {
        RibbonLayout.FindItem("insert.block")!.Verb.ShouldBe(ShellVerb.Of(InsertKind.WorldBrush));
        RibbonLayout.FindItem("history.undo")!.Verb.ShouldBe(ShellVerb.Of(EditorHostCommand.Undo));
        RibbonLayout.FindItem("camera.frameall")!.Verb.ShouldBe(ShellVerb.Of(EditorCameraCommand.FrameAll));
        RibbonLayout.FindItem("nothing.at.all").ShouldBeNull();
        RibbonLayout.FindItem(null).ShouldBeNull();
    }

    // Which markup file draws which page. A tab missing here fails the test below.
    private static readonly (string TabId, string File)[] PageFiles =
    [
        (RibbonLayout.DefaultTabId, "RibbonBuildTab.axaml"),
        (RibbonLayout.ViewTabId, "RibbonViewTab.axaml"),
    ];

    [Fact]
    public void Every_page_draws_exactly_the_controls_its_roster_promises()
    {
        // The page also checks this at construction. This is the same check
        // from the sources, with no Avalonia application.
        RibbonLayout.Tabs.Select(t => t.Id)
            .ShouldBe(PageFiles.Select(p => p.TabId), "every tab needs a markup file listed here");

        foreach ((string tabId, string file) in PageFiles)
        {
            RibbonTab tab = RibbonLayout.FindTab(tabId)!;
            List<string> tags = TagsIn(Path.Combine(RibbonFolder(), file));

            tags.GroupBy(t => t, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ShouldBeEmpty($"{file} draws an id more than once");

            tags.Order(StringComparer.Ordinal).ShouldBe(
                RibbonLayout.ItemsOf(tab).Select(i => i.Id).Order(StringComparer.Ordinal),
                $"{file} and the '{tabId}' roster must name the same controls");
        }
    }

    [Fact]
    public void The_always_visible_pair_is_drawn_on_the_strip_itself()
    {
        // The strip lives in the window, so it survives a collapse.
        string window = File.ReadAllText(Path.Combine(SourceRoot(), "SpectraEngine.Editor", "MainWindow.axaml"));
        List<string> pageTags =
        [
            .. PageFiles.SelectMany(p => TagsIn(Path.Combine(RibbonFolder(), p.File))),
        ];

        foreach (RibbonItem item in RibbonLayout.AlwaysVisible)
        {
            window.ShouldContain($"Tag=\"{item.Id}\"", customMessage: $"{item.Id} must be on the tab strip");
            pageTags.ShouldNotContain(item.Id, $"{item.Id} must not also be drawn on a page");
        }
    }

    // Page widths are measured in the render suite (RibbonWidthTests).

    [Fact]
    public void Every_page_leads_with_a_large_control()
    {
        foreach (RibbonTab tab in RibbonLayout.Tabs)
        {
            tab.Groups[0].Items
                .Any(i => i.Size == RibbonItemSize.Large)
                .ShouldBeTrue($"the first group of '{tab.Id}' should lead with a large control");
        }
    }

    [Fact]
    public void A_large_label_cannot_wrap_to_a_third_line()
    {
        // MaxLines is 2, so a third line is dropped without any visible clip.
        // A character count is a proxy; RibbonLabelTests measures the real layout.
        var offenders = new List<string>();

        foreach (RibbonItem item in AllItems().Where(i => i.Size == RibbonItemSize.Large))
        {
            if (item.Label.Length > RibbonLayout.LargeLabelLimit)
                offenders.Add($"{item.Id}: '{item.Label}' is {item.Label.Length} characters");
        }

        offenders.ShouldBeEmpty(
            $"a large button's label must fit two lines at {RibbonLayout.LargeLabelLimit} characters");
    }

    [Fact]
    public void Every_control_wears_the_class_its_declared_kind_requires()
    {
        // The Tag check allows any control under a valid id. A check row drawn
        // as a plain button posts the right verb and never lights.
        var offenders = new List<string>();

        foreach ((string tabId, string file) in PageFiles)
        {
            RibbonTab tab = RibbonLayout.FindTab(tabId)!;
            string markup = File.ReadAllText(Path.Combine(RibbonFolder(), file));

            foreach (RibbonItem item in RibbonLayout.ItemsOf(tab))
            {
                string required = RibbonLayout.RequiredClass(item);
                if (!ClassesOn(markup, item.Id).Contains(required, StringComparer.Ordinal))
                    offenders.Add($"{file}: {item.Id} is a {item.Kind} and must wear '{required}'");
            }
        }

        offenders.ShouldBeEmpty("a declared kind must be the control that is actually drawn");
    }

    [Fact]
    public void A_lit_control_is_bound_to_something_that_lights_it()
    {
        // These are Buttons wearing Classes.active, not ToggleButtons, so a
        // forgotten binding still posts its verb and never lights.
        var offenders = new List<string>();

        foreach ((string tabId, string file) in PageFiles)
        {
            RibbonTab tab = RibbonLayout.FindTab(tabId)!;
            string markup = File.ReadAllText(Path.Combine(RibbonFolder(), file));

            foreach (RibbonItem item in RibbonLayout.ItemsOf(tab))
            {
                if (item.Kind is not (RibbonControlKind.Toggle or RibbonControlKind.Check
                    or RibbonControlKind.Radio))
                {
                    continue;
                }

                if (!ElementFor(markup, item.Id).Contains("Classes.active=", StringComparison.Ordinal))
                    offenders.Add($"{file}: {item.Id} is a {item.Kind} with nothing to light it");
            }
        }

        offenders.ShouldBeEmpty("a Toggle, Check or Radio must bind Classes.active");
    }

    // The opening tag of the element carrying the id, as text. Not a XAML parse.
    private static string ElementFor(string markup, string id)
    {
        int tag = markup.IndexOf($"Tag=\"{id}\"", StringComparison.Ordinal);
        if (tag < 0) return string.Empty;

        int open = markup.LastIndexOf('<', tag);
        int close = markup.IndexOf('>', tag);
        if (open < 0 || close < 0) return string.Empty;

        return markup[open..close];
    }

    private static IReadOnlyList<string> ClassesOn(string markup, string id)
    {
        Match m = Regex.Match(ElementFor(markup, id), "Classes=\"([^\"]*)\"");
        return m.Success
            ? m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : [];
    }

    private static IEnumerable<RibbonItem> AllItems() =>
        RibbonLayout.AlwaysVisible.Concat(RibbonLayout.Tabs.SelectMany(RibbonLayout.ItemsOf));

    private static List<string> TagsIn(string file)
    {
        File.Exists(file).ShouldBeTrue($"expected the ribbon page markup at {file}");

        return Regex.Matches(File.ReadAllText(file), "Tag=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    private static string RibbonFolder() =>
        Path.Combine(SourceRoot(), "SpectraEngine.Editor", "Shell", "Ribbon");

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("no solution file above the test binary");
    }
}

/// <summary>
/// The ribbon's collapse state machine, and the pin state it persists.
/// </summary>
public sealed class RibbonSurfaceTests
{
    [Fact]
    public void Expanded_shows_the_active_page_inline()
    {
        RibbonSurfaceState state = RibbonSurface.Create(expanded: true);

        RibbonSurface.HostFor(state).ShouldBe(RibbonBodyHost.Inline);
        state.FlyoutOpen.ShouldBeFalse();
    }

    [Fact]
    public void Collapsed_shows_nothing_but_the_strip()
    {
        RibbonSurfaceState state = RibbonSurface.Create(expanded: false);

        RibbonSurface.HostFor(state).ShouldBe(RibbonBodyHost.None);
    }

    [Fact]
    public void Selecting_a_tab_while_expanded_is_navigation()
    {
        RibbonSurfaceState state = RibbonSurface.Create(expanded: true);
        state = RibbonSurface.SelectTab(state, RibbonLayout.ViewTabId);

        state.ActiveTabId.ShouldBe(RibbonLayout.ViewTabId);
        state.FlyoutOpen.ShouldBeFalse("an expanded ribbon has nowhere to fly a page out to");
        RibbonSurface.HostFor(state).ShouldBe(RibbonBodyHost.Inline);
    }

    [Fact]
    public void Selecting_a_tab_while_collapsed_flies_that_page_out()
    {
        RibbonSurfaceState state = RibbonSurface.Create(expanded: false);
        state = RibbonSurface.SelectTab(state, RibbonLayout.ViewTabId);

        state.ActiveTabId.ShouldBe(RibbonLayout.ViewTabId);
        RibbonSurface.HostFor(state).ShouldBe(RibbonBodyHost.Flyout);
        state.Expanded.ShouldBeFalse("flying a page out is not the same as pinning it open");
    }

    [Fact]
    public void Clicking_the_tab_that_is_already_flown_out_puts_it_away()
    {
        RibbonSurfaceState state = RibbonSurface.Create(expanded: false);
        state = RibbonSurface.SelectTab(state, RibbonLayout.DefaultTabId);
        state = RibbonSurface.SelectTab(state, RibbonLayout.DefaultTabId);

        RibbonSurface.HostFor(state).ShouldBe(RibbonBodyHost.None);
    }

    [Fact]
    public void Clicking_the_other_tab_while_one_is_flown_out_switches_pages()
    {
        RibbonSurfaceState state = RibbonSurface.Create(expanded: false);
        state = RibbonSurface.SelectTab(state, RibbonLayout.DefaultTabId);
        state = RibbonSurface.SelectTab(state, RibbonLayout.ViewTabId);

        state.ActiveTabId.ShouldBe(RibbonLayout.ViewTabId);
        RibbonSurface.HostFor(state).ShouldBe(RibbonBodyHost.Flyout);
    }

    [Fact]
    public void An_unknown_tab_id_changes_nothing()
    {
        RibbonSurfaceState state = RibbonSurface.Create(expanded: true);
        RibbonSurface.SelectTab(state, "retired").ShouldBe(state);
        RibbonSurface.SelectTab(state, null).ShouldBe(state);
    }

    [Fact]
    public void Expanding_closes_a_flyout_rather_than_leaving_two_copies_of_one_page()
    {
        RibbonSurfaceState state = RibbonSurface.Create(expanded: false);
        state = RibbonSurface.SelectTab(state, RibbonLayout.ViewTabId);
        state = RibbonSurface.SetExpanded(state, expanded: true);

        state.FlyoutOpen.ShouldBeFalse();
        RibbonSurface.HostFor(state).ShouldBe(RibbonBodyHost.Inline);
        state.ActiveTabId.ShouldBe(RibbonLayout.ViewTabId, "expanding keeps the page that was showing");
    }

    [Fact]
    public void Setting_the_pin_is_idempotent_because_it_is_a_set_verb()
    {
        RibbonSurfaceState state = RibbonSurface.Create(expanded: true);
        RibbonSurface.SetExpanded(RibbonSurface.SetExpanded(state, true), true).ShouldBe(state);
    }

    [Fact]
    public void Invoking_a_command_closes_a_flown_out_page_and_leaves_a_pinned_one()
    {
        RibbonSurfaceState collapsed = RibbonSurface.SelectTab(
            RibbonSurface.Create(expanded: false), RibbonLayout.DefaultTabId);
        RibbonSurface.HostFor(RibbonSurface.Invoke(collapsed)).ShouldBe(RibbonBodyHost.None);

        RibbonSurfaceState pinned = RibbonSurface.Create(expanded: true);
        RibbonSurface.Invoke(pinned).ShouldBe(pinned, "a pinned ribbon does not put itself away");
    }

    [Fact]
    public void Dismissing_closes_a_flyout_and_touches_nothing_else()
    {
        RibbonSurfaceState state = RibbonSurface.SelectTab(
            RibbonSurface.Create(expanded: false), RibbonLayout.ViewTabId);

        RibbonSurfaceState dismissed = RibbonSurface.Dismiss(state);
        dismissed.FlyoutOpen.ShouldBeFalse();
        dismissed.ActiveTabId.ShouldBe(RibbonLayout.ViewTabId);
        dismissed.Expanded.ShouldBeFalse();
    }

    [Fact]
    public void The_pin_survives_a_restart_and_the_active_page_deliberately_does_not()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "spectra-tests", Path.GetRandomFileName(), "editor.json");

        var settings = new EditorSettings();
        settings.RibbonExpanded.ShouldBeTrue("the ribbon ships open, showing what it can do");

        settings.SetRibbonExpanded(false);
        settings.Save(path, NullLogger.Instance);

        EditorSettings reloaded = EditorSettings.Load(path, NullLogger.Instance);
        reloaded.RibbonExpanded.ShouldBeFalse();

        RibbonSurfaceState state = RibbonSurface.Create(reloaded.RibbonExpanded);
        state.Expanded.ShouldBeFalse();
        state.ActiveTabId.ShouldBe(RibbonLayout.DefaultTabId);
    }

    [Fact]
    public void A_settings_file_that_never_heard_of_the_ribbon_opens_it()
    {
        string dir = Path.Combine(Path.GetTempPath(), "spectra-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "editor.json");
        File.WriteAllText(path, """{"recentProjects":[]}""");

        EditorSettings.Load(path, NullLogger.Instance).RibbonExpanded.ShouldBeTrue();
    }
}
