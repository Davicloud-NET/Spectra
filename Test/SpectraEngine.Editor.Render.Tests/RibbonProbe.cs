using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>One headless Avalonia session, shared by the whole assembly.</summary>
public sealed class RibbonSession : IDisposable
{
    private readonly HeadlessUnitTestSession _session =
        HeadlessUnitTestSession.StartNew(typeof(RibbonHarness), AvaloniaTestIsolationLevel.PerAssembly);

    /// <summary>Runs <paramref name="body"/> on the session's UI thread and rethrows here.</summary>
    public void On(Action body) => _session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public void Dispose() => _session.Dispose();
}

/// <summary>The collection every test in this suite joins.</summary>
[CollectionDefinition(Name)]
public sealed class RibbonSessionCollection : ICollectionFixture<RibbonSession>
{
    /// <summary>The collection's name.</summary>
    public const string Name = "ribbon-render";
}

/// <summary>One ribbon page, laid out inside a window, ready to be measured.</summary>
public sealed class RibbonProbe : IDisposable
{
    private RibbonProbe(Window window, Border body, RibbonTabView page, ShellModel model)
    {
        Window = window;
        Body = body;
        Page = page;
        Model = model;
    }

    /// <summary>The host window. Wide enough that nothing is clamped.</summary>
    public Window Window { get; }

    /// <summary>The page inside <c>Border.ribbonbody</c>, exactly as the shell hosts it.</summary>
    public Border Body { get; }

    /// <summary>The page under test.</summary>
    public RibbonTabView Page { get; }

    /// <summary>The model the page's bindings resolve against.</summary>
    public ShellModel Model { get; }

    /// <summary>
    /// Builds a page, puts it in a window and forces one layout pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three things here are load-bearing rather than scene-setting. The page
    /// must sit inside a <c>Window</c>, because <c>Controls.axaml</c>'s
    /// <c>Style Selector="Window"</c> is where <c>FontFamily</c>,
    /// <c>FontSize</c> and <c>Foreground</c> come from - measured outside one,
    /// every label inherits the platform default face and every number is
    /// wrong. It must sit inside <c>Border.ribbonbody</c>, whose padding is
    /// part of what has to fit the window's minimum. And the DataContext is
    /// not decoration: the two chips render <c>{Binding Orientation}</c> and
    /// <c>{Binding GizmoStyle}</c>, so a null model measures a page that never
    /// exists on screen.
    /// </para>
    /// <para>
    /// <b>Constructing the page runs <c>ValidateAgainstRoster</c></b>, which is
    /// the runtime half of the roster weld reaching CI for the first time: an
    /// id drawn twice, a tagged control the roster has never heard of, a roster
    /// entry with no control, or a control not wearing the class its declared
    /// kind requires all refuse here exactly as they refuse the shipped window.
    /// </para>
    /// </remarks>
    public static RibbonProbe Open(string tabId, double scaling = 1.0, Action<ShellModel>? drive = null)
    {
        var model = new ShellModel();
        drive?.Invoke(model);

        RibbonTabView page = tabId == RibbonLayout.DefaultTabId
            ? new RibbonBuildTab()
            : new RibbonViewTab();
        page.DataContext = model;

        // The snap field is written imperatively by MainWindow.RefreshSnapField
        // rather than bound, so a page opened without one shows an empty box -
        // which is a hole in the sheet that the shipped surface does not have.
        // Write what the shell writes, through the same formatter.
        if (page is RibbonBuildTab build)
            build.SnapField.Text = PropertyFieldModel.Format(model.SnapIncrement);

        var body = new Border { Child = page };
        body.Classes.Add("ribbonbody");

        // 2600 so the page's horizontal StackPanel is never clamped and its
        // DesiredSize is its true width rather than the window's.
        var window = new Window { Width = 2600, Height = 400, Content = body };

        // Before Show: UseLayoutRounding rounds to the device pixel grid, so the
        // scaling is the one thing that can move an integer result between
        // machines. Pinning it turns DPI from a source of flake into a parameter.
        window.SetRenderScaling(scaling);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return new RibbonProbe(window, body, page, model);
    }

    /// <summary>Every control the roster names, found the way the validator finds them.</summary>
    public IEnumerable<Control> Tagged() =>
        Page.GetVisualDescendants().OfType<Control>().Where(c => c.Tag is string);

    /// <summary>The control carrying <paramref name="id"/>.</summary>
    public Control ByTag(string id) =>
        Tagged().FirstOrDefault(c => (string)c.Tag! == id)
        ?? throw new InvalidOperationException($"no control on '{Page.GetType().Name}' carries Tag=\"{id}\"");

    /// <summary>The layout box of <paramref name="child"/> in <paramref name="root"/>'s space.</summary>
    public static Rect BoundsIn(Visual child, Visual root) =>
        child.TransformToVisual(root) is { } m
            ? new Rect(child.Bounds.Size).TransformToAABB(m)
            : throw new InvalidOperationException("no transform between the two visuals");

    /// <summary>
    /// The INK of a shape in <paramref name="root"/>'s space - where the stroke
    /// actually lands, not the box layout gave it.
    /// </summary>
    /// <remarks>
    /// <b>This is the distinction the caret defect lived in.</b> A
    /// <see cref="Shape"/> with <c>Stretch="None"</c> draws its geometry at
    /// AUTHORED coordinates inside whatever box it was arranged into, so the box
    /// can be perfectly centred while the ink sits 28px to its left. Nothing that
    /// reads <c>Bounds</c> can see that; this can.
    /// </remarks>
    public static Rect InkIn(Shape shape, Visual root)
    {
        var pen = new Pen(Brushes.Black, shape.StrokeThickness,
                          lineCap: shape.StrokeLineCap, lineJoin: shape.StrokeJoin);
        Rect ink = shape.RenderedGeometry?.GetRenderBounds(pen) ?? default;
        return shape.TransformToVisual(root) is { } m ? ink.TransformToAABB(m) : default;
    }

    /// <inheritdoc/>
    public void Dispose() => Window.Close();
}
