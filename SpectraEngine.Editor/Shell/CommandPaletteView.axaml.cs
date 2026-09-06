using Avalonia.Controls;

namespace SpectraEngine.Editor.Shell;

/// <summary>The command palette's surface: a query box over a list of rows.</summary>
/// <remarks>
/// No behaviour of its own. The window owns the keyboard, the search and the
/// dispatch, exactly as it owns the ribbon's; this exists so the markup is
/// declared once and can be constructed by something other than a window.
///
/// Deliberately NO hand-written InitializeComponent: a parameterless one shadows
/// the generated overload, the XAML loads, and every x:Name field stays null -
/// which is how NameDialog threw a NullReferenceException on its first line for
/// its whole life.
/// </remarks>
public partial class CommandPaletteView : UserControl
{
    /// <summary>Creates the view.</summary>
    public CommandPaletteView() => InitializeComponent();

    /// <summary>The query box. The window wires its keyboard.</summary>
    public TextBox QueryBox => Query;

    /// <summary>The list of matching commands.</summary>
    public ListBox RowList => Rows;

    /// <summary>The line saying how many matches are not shown.</summary>
    public TextBlock FooterLabel => Footer;
}
