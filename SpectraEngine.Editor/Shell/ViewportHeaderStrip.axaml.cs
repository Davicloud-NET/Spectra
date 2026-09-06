using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>What a control on the viewport header asks for.</summary>
/// <remarks>
/// <b>A closed enum rather than the handler's name as a string.</b> The strip
/// knows which control was pressed and the window is the only thing that knows
/// whether there is a session, what the current state is and which optimistic
/// value to hold - so the strip raises an intent and the window dispatches it
/// through the handlers it already had. A string would compile with a typo in
/// it and do nothing.
/// </remarks>
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

/// <summary>The row above the picture: view-scoped state and readouts.</summary>
/// <remarks>
/// No behaviour of its own, exactly like the ribbon pages: every control raises
/// an intent the window dispatches. This exists so the markup is declared once
/// and can be constructed - and MEASURED - by something other than a window.
///
/// Deliberately NO hand-written InitializeComponent: a parameterless one shadows
/// the generated overload, the XAML loads, and every x:Name field stays null.
/// </remarks>
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
