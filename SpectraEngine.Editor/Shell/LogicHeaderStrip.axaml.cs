using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The row above the Logic view: its title and the button that hides it.
/// </summary>
// Don't hand-write InitializeComponent: a parameterless one shadows the
// generated overload and every x:Name field stays null.
public partial class LogicHeaderStrip : UserControl
{
    /// <summary>Creates the strip.</summary>
    public LogicHeaderStrip() => InitializeComponent();

    /// <summary>Raised when the hide button is pressed.</summary>
    public event Action? HideRequested;

    private void OnHideClicked(object? sender, RoutedEventArgs e) => HideRequested?.Invoke();
}
