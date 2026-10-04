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

/// <summary>The viewport header strip, measured at the narrowest viewport the shell allows.</summary>
// Needs real Skia: the headless stub typeface gives every glyph the same advance.
[Collection(RibbonSessionCollection.Name)]
public sealed class HeaderStripWidthTests(RibbonSession session)
{
    // Compact workspace at the window's minimum size.
    private static double NarrowestViewport()
    {
        WorkspaceMetrics metrics = WorkspaceLayout.For(WorkspacePreset.Compact);
        (double width, _) = WorkspaceLayout.ViewportCell(
            metrics, 1180, 640, new WorkspaceChrome(Vertical: 224, Horizontal: 32));

        return width;
    }

    private static (ViewportHeaderStrip Strip, Window Window) Open(ShellModel model)
    {
        var strip = new ViewportHeaderStrip { DataContext = model };

        // Must sit in a Window: the font comes from Controls.axaml's Window selector.
        var window = new Window { Width = 2600, Height = 200, Content = strip };
        window.SetRenderScaling(1.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        strip.Measure(new Size(double.PositiveInfinity, 28));
        return (strip, window);
    }

    // Compact workspace at the default window size.
    private static double DefaultViewport()
    {
        WorkspaceMetrics metrics = WorkspaceLayout.For(WorkspacePreset.Compact);
        (double width, _) = WorkspaceLayout.ViewportCell(
            metrics, 1480, 920, new WorkspaceChrome(Vertical: 224, Horizontal: 32));

        return width;
    }

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

        // Widest state: every overlay on, longest pipeline name, widest position.
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

            // Past two overlays the chips collapse into one.
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
                // Arranged at the real width: a strip that fits overall can
                // still trim one label.
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
                // The row is fixed, so a taller strip would take height off the viewport.
                strip.DesiredSize.Height.ShouldBe(28);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
