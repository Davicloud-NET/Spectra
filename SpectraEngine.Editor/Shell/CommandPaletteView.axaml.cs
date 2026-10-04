using Avalonia.Controls;

namespace SpectraEngine.Editor.Shell;

/// <summary>The command palette's surface: a query box over a list of rows.</summary>
// The window owns the keyboard, the search and the dispatch.
// Do not hand-write InitializeComponent: a parameterless one shadows the
// generated overload and every x:Name field stays null.
public partial class CommandPaletteView : UserControl
{
    /// <summary>Creates the view.</summary>
    public CommandPaletteView() => InitializeComponent();

    /// <summary>The query box.</summary>
    public TextBox QueryBox => Query;

    /// <summary>The list of matching commands.</summary>
    public ListBox RowList => Rows;

    /// <summary>The line saying how many matches are not shown.</summary>
    public TextBlock FooterLabel => Footer;
}
