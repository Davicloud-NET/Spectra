using Avalonia.Input;
using Avalonia.Interactivity;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor;

// The view panes in the centre of the window: the 3D view and the Logic view.
public partial class MainWindow
{
    private void OnShowLogicBelow(object? sender, RoutedEventArgs e) =>
        RunWorkspaceVerb(WorkspaceCommand.ShowLogicBelow);

    private void OnShowLogicBeside(object? sender, RoutedEventArgs e) =>
        RunWorkspaceVerb(WorkspaceCommand.ShowLogicBeside);

    private void OnHideLogic(object? sender, RoutedEventArgs e) =>
        RunWorkspaceVerb(WorkspaceCommand.HideLogic);

    private void OnViewSplitterDragCompleted(object? sender, VectorEventArgs e) =>
        OnSplitterDragCompleted(sender, e);
}
