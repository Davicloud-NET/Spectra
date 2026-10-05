using Avalonia.Controls;

namespace SpectraEngine.Editor.Shell.Ribbon;

/// <summary>
/// The Build page: insert, transform, snap, arrange. Everything on it changes
/// the level.
/// </summary>
// Don't hand-write InitializeComponent(): it shadows the generated overload
// and every x:Name field stays null.
public partial class RibbonBuildTab : RibbonTabView
{
    public RibbonBuildTab()
    {
        InitializeComponent();
        ValidateAgainstRoster();
    }

    /// <inheritdoc/>
    protected override string TabId => RibbonLayout.DefaultTabId;

    /// <summary>The snap increment field. The window owns its commit rule.</summary>
    public TextBox SnapField => SnapBox;

    /// <summary>
    /// The Entity split button's caret half. The window fills its class list
    /// from the live session.
    /// </summary>
    public Button EntityCaretButton => EntityCaret;

    /// <summary>The split button's main half, whose tooltip names the live class.</summary>
    public Button EntityInsertButton => EntityInsert;

    /// <summary>
    /// The Make entity row. The window opens its class list from the live
    /// session.
    /// </summary>
    public Button MakeEntityButton => MakeEntity;
}
