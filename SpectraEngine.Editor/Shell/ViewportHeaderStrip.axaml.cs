using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>What a control on the viewport header asks for.</summary>
public enum HeaderAction
{
    ToggleNavigation,

    GridAuto,
    GridOn,
    GridOff,

    ViewPerspective,
    ViewTop,
    ViewBottom,
    ViewFront,
    ViewBack,
    ViewRight,
    ViewLeft,

    DebugWireframe,
    DebugVertices,
    DebugAabbs,
    DebugNormals,
    DebugSceneGraph,
}

/// <summary>
/// The row above the picture: view-scoped state and readouts. Every control
/// raises a <see cref="HeaderAction"/> the window dispatches.
/// </summary>
// Don't hand-write InitializeComponent: a parameterless one shadows the
// generated overload and every x:Name field stays null.
public partial class ViewportHeaderStrip : UserControl
{
    /// <summary>Creates the strip.</summary>
    public ViewportHeaderStrip() => InitializeComponent();

    /// <summary>Raised when a control on the strip is pressed.</summary>
    public event Action<HeaderAction>? Activated;

    private void OnToggleNavigationClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.ToggleNavigation);

    private void OnGridAutoClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.GridAuto);
    private void OnGridOnClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.GridOn);
    private void OnGridOffClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.GridOff);

    private void OnViewPerspectiveClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.ViewPerspective);
    private void OnViewTopClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.ViewTop);
    private void OnViewBottomClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.ViewBottom);
    private void OnViewFrontClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.ViewFront);
    private void OnViewBackClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.ViewBack);
    private void OnViewRightClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.ViewRight);
    private void OnViewLeftClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.ViewLeft);

    private void OnDebugWireClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.DebugWireframe);
    private void OnDebugVerticesClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.DebugVertices);
    private void OnDebugAabbsClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.DebugAabbs);
    private void OnDebugNormalsClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.DebugNormals);
    private void OnDebugSceneGraphClicked(object? s, RoutedEventArgs e) => Raise(HeaderAction.DebugSceneGraph);

    private void Raise(HeaderAction action) => Activated?.Invoke(action);
}
