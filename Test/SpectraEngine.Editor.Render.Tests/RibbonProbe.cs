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

    /// <summary>
    /// Runs <paramref name="body"/> on the session's UI thread and blocks
    /// until it has finished, for a test that waits on work the shell does
    /// off that thread.
    /// </summary>
    // Blocks, and is not awaited: an awaiting test would carry on inline on
    // the session's thread, and the suite would then end by waiting on itself.
    public void On(Func<Task> body) =>
        _session.Dispatch(
            async () =>
            {
                await body();
                return true;
            },
            CancellationToken.None).GetAwaiter().GetResult();

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

    /// <summary>The <c>Border.ribbonbody</c> hosting the page, as the shell hosts it.</summary>
    public Border Body { get; }

    /// <summary>The page under test.</summary>
    public RibbonTabView Page { get; }

    /// <summary>The model the page's bindings resolve against.</summary>
    public ShellModel Model { get; }

    /// <summary>Builds a page, puts it in a window and forces one layout pass.</summary>
    // The Window supplies the font (Controls.axaml's Window selector), the
    // ribbonbody border supplies padding that counts toward the width, and the
    // chips bind to the model, so none of the three can be left out.
    public static RibbonProbe Open(string tabId, double scaling = 1.0, Action<ShellModel>? drive = null)
    {
        var model = new ShellModel();
        drive?.Invoke(model);

        RibbonTabView page = tabId == RibbonLayout.DefaultTabId
            ? new RibbonBuildTab()
            : new RibbonViewTab();
        page.DataContext = model;

        // The snap field is not bound; MainWindow.RefreshSnapField writes it.
        // Write the same text here or the box is empty.
        if (page is RibbonBuildTab build)
            build.SnapField.Text = PropertyFieldModel.Format(model.SnapIncrement);

        var body = new Border { Child = page };
        body.Classes.Add("ribbonbody");

        // Wide enough that DesiredSize is the page's true width.
        var window = new Window { Width = 2600, Height = 400, Content = body };

        // Before Show. Layout rounds to device pixels, so pin the scaling or
        // results differ between machines.
        window.SetRenderScaling(scaling);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return new RibbonProbe(window, body, page, model);
    }

    /// <summary>Every control carrying a roster id in its <c>Tag</c>.</summary>
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
    /// Where a shape's stroke lands in <paramref name="root"/>'s space, which
    /// can differ from its layout box when <c>Stretch="None"</c>.
    /// </summary>
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
