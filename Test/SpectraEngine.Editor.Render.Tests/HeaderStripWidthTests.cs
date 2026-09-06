using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using SpectraEngine.Core.Scene;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpectraEngine.Editor.Shell;
using System.Linq;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The viewport header strip, measured at the narrowest viewport the shell can
/// produce.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the shell's most crowded row and every stage adds to it.</b> It
/// carries the pipeline dropdown, three chips, up to five latched overlay chips
/// and a fixed-width camera readout; the compact workspace at the window's own
/// 1180px minimum gives the viewport 644px. Whether it still fits is arithmetic
/// somebody would otherwise discover on a smaller monitor than the one it was
/// designed on.
/// </para>
/// <para>
/// <b>Real Skia, not the headless drawing stub.</b> The stub's typeface gives
/// every glyph the same advance, so a width measured under it is an invented
/// model wearing the framework's name.
/// </para>
/// </remarks>
[Collection(RibbonSessionCollection.Name)]
public sealed class HeaderStripWidthTests(RibbonSession session)
{
    /// <summary>
    /// The viewport cell in the compact workspace at the window's minimum.
    /// </summary>
    /// <remarks>
    /// Computed rather than typed, so the two arithmetics cannot drift: this is
    /// the same call the workspace tests make and the same one the shell lays
    /// out from.
    /// </remarks>
    private static double NarrowestViewport()
    {
        WorkspaceMetrics metrics = WorkspaceLayout.For(WorkspacePreset.Compact);
        (double width, _) = WorkspaceLayout.ViewportCell(
            metrics, 1180, 640, new WorkspaceChrome(Vertical: 241, Horizontal: 6));

        return width;
    }

    private static (ViewportHeaderStrip Strip, Window Window) Open(ShellModel model)
    {
        var strip = new ViewportHeaderStrip { DataContext = model };

        // Inside a Window, because Controls.axaml's Window selector is where the
        // font face, size and foreground come from: measured outside one, every
        // label inherits the platform default and every number is wrong.
        var window = new Window { Width = 2600, Height = 200, Content = strip };
        window.SetRenderScaling(1.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        strip.Measure(new Size(double.PositiveInfinity, 28));
        return (strip, window);
    }

    /// <summary>The viewport cell in the compact workspace at the default window.</summary>
    private static double DefaultViewport()
    {
        WorkspaceMetrics metrics = WorkspaceLayout.For(WorkspacePreset.Compact);
        (double width, _) = WorkspaceLayout.ViewportCell(
            metrics, 1480, 920, new WorkspaceChrome(Vertical: 241, Horizontal: 6));

        return width;
    }

    /// <summary>A session with nothing latched: what the strip shows most of the time.</summary>
    private static ShellModel AtRest()
    {
        var model = new ShellModel { HasSession = true };

        model.ApplySnapshot(new Core.Hosting.FrameSnapshot
        {
            ViewName = "Perspective",
            GridModeName = "auto",
            NavigationModeName = "editor freelook",
            PipelineNames = ["Deferred", "Forward", "Wireframe"],
            PipelineName = "Deferred",
            CameraPosition = new System.Numerics.Vector3(-12345.6f, -12345.6f, -12345.6f),
        });

        return model;
    }

    private static ShellModel Busy()
    {
        var model = new ShellModel { HasSession = true };

        // The widest state the strip can reach: every overlay latched, the
        // longest pipeline name, and a camera position with three negative
        // five-figure coordinates.
        model.ApplySnapshot(new Core.Hosting.FrameSnapshot
        {
            ViewName = "Perspective",
            GridModeName = "auto",
            NavigationModeName = "editor freelook",
            PipelineNames = ["Deferred", "Forward", "Wireframe"],
            PipelineName = "Wireframe",
            DebugFlags =
                DebugVisualization.Wireframe |
                DebugVisualization.Vertices |
                DebugVisualization.Aabbs |
                DebugVisualization.Normals |
                DebugVisualization.SceneGraph,
            CameraPosition = new System.Numerics.Vector3(-12345.6f, -12345.6f, -12345.6f),
        });

        return model;
    }

    [Fact]
    public void The_resting_strip_fits_the_narrowest_viewport()
    {
        session.On(() =>
        {
            (ViewportHeaderStrip strip, Window window) = Open(AtRest());

            try
            {
                double available = NarrowestViewport();

                // The state the strip is in almost all the time, at the
                // smallest viewport the shell can produce: 644px, which is the
                // compact workspace at the window's own 1180px minimum.
                strip.DesiredSize.Width.ShouldBeLessThanOrEqualTo(
                    available,
                    $"the strip wants {strip.DesiredSize.Width:0.#}px and the narrowest " +
                    $"viewport is {available:0.#}px");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_busiest_strip_fits_the_default_window()
    {
        session.On(() =>
        {
            (ViewportHeaderStrip strip, Window window) = Open(Busy());

            try
            {
                // Every overlay latched and the longest pipeline name. This is
                // the honest second bound: between the two, the camera readout
                // is what the Grid gives way on, and the OVERLAY half is what
                // the collapse bounds - five chips wanted 340px and one wants
                // about 90, which is the difference between a strip that grows
                // without limit and one that grows once.
                strip.DesiredSize.Width.ShouldBeLessThanOrEqualTo(
                    DefaultViewport(),
                    $"the strip wants {strip.DesiredSize.Width:0.#}px");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Collapsing_the_overlays_is_what_bounds_the_variable_half()
    {
        session.On(() =>
        {
            (ViewportHeaderStrip busy, Window busyWindow) = Open(Busy());
            double withFive = busy.DesiredSize.Width;
            busyWindow.Close();

            var two = new ShellModel { HasSession = true };
            two.ApplySnapshot(new Core.Hosting.FrameSnapshot
            {
                ViewName = "Perspective",
                GridModeName = "auto",
                NavigationModeName = "editor freelook",
                PipelineNames = ["Deferred", "Forward", "Wireframe"],
                PipelineName = "Wireframe",
                DebugFlags = DebugVisualization.Wireframe | DebugVisualization.Vertices,
                CameraPosition = new System.Numerics.Vector3(-12345.6f, -12345.6f, -12345.6f),
            });

            (ViewportHeaderStrip pair, Window pairWindow) = Open(two);
            double withTwo = pair.DesiredSize.Width;
            pairWindow.Close();

            // Five overlays must not be wider than two: past the threshold they
            // are one chip, so the strip grows once and then stops. Without the
            // collapse this difference was 195px.
            withFive.ShouldBeLessThanOrEqualTo(
                withTwo, $"five overlays want {withFive:0}px and two want {withTwo:0}px");
        });
    }

    [Fact]
    public void Nothing_on_the_strip_is_trimmed_at_that_width()
    {
        session.On(() =>
        {
            (ViewportHeaderStrip strip, Window window) = Open(AtRest());

            try
            {
                // Laid out at the real width rather than measured unbounded: a
                // strip that FITS can still ellipsise a chip if one of its
                // labels is given less than it asked for.
                strip.Arrange(new Rect(0, 0, NarrowestViewport(), 28));
                Dispatcher.UIThread.RunJobs();

                foreach (TextBlock label in strip.GetVisualDescendants().OfType<TextBlock>())
                {
                    if (!label.IsVisible || label.Text is not { Length: > 0 }) continue;

                    foreach (TextLine line in label.TextLayout.TextLines)
                    {
                        line.HasCollapsed.ShouldBeFalse(
                            $"'{label.Text}' is trimmed at {NarrowestViewport():0}px");
                    }
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_strip_is_the_row_height_it_declares()
    {
        session.On(() =>
        {
            (ViewportHeaderStrip strip, Window window) = Open(Busy());

            try
            {
                // 28px, and it matters: the strip sits above the viewport in a
                // fixed row, so a taller one silently takes pixels off the
                // picture rather than reporting anything.
                strip.DesiredSize.Height.ShouldBe(28);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
