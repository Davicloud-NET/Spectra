using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The status bar's slot for a sound being listened to: its file name, and a
/// press stops it. Hidden while nothing plays. Its <c>DataContext</c> is a
/// <see cref="SoundPreviewModel"/>.
/// </summary>
// Don't hand-write InitializeComponent: a parameterless one shadows the
// generated overload and every x:Name field stays null.
public partial class SoundPreviewStatus : UserControl
{
    /// <summary>Creates the slot.</summary>
    public SoundPreviewStatus() => InitializeComponent();

    private void OnStopPressed(object? sender, RoutedEventArgs e) =>
        (DataContext as SoundPreviewModel)?.Stop();
}
