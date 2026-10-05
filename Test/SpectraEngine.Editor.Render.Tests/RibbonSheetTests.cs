using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Entities;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// Rasterises shell surfaces to PNG so a person can look at them. Only asserts
/// that a frame was drawn at the expected size and is not one flat colour.
/// </summary>
// No golden images: the transitions make a frame differ by a few per cent of
// alpha from tick to tick.
[Collection(RibbonSessionCollection.Name)]
public sealed class RibbonSheetTests(RibbonSession session)
{
    /// <summary>Where the sheets land. Gitignored; CI uploads it as an artifact.</summary>
    public static string OutputDirectory { get; } = FindArtifacts();

    [Theory]
    [InlineData("build", "rest", 1.0)]
    [InlineData("view", "rest", 1.0)]
    [InlineData("build", "rest", 2.0)]
    [InlineData("view", "rest", 2.0)]
    [InlineData("build", "active", 1.0)]
    [InlineData("view", "active", 1.0)]
    [InlineData("build", "empty", 1.0)]
    [InlineData("view", "empty", 1.0)]
    [InlineData("build", "working", 2.0)]
    [InlineData("view", "working", 2.0)]
    public void Both_pages_rasterise_into_a_sheet_a_person_can_look_at(string tabId, string state, double scaling)
    {
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId, scaling, Drive(state));

            // Shrink the 2600px probe window to the page.
            Size want = probe.Body.DesiredSize;
            probe.Window.Width = Math.Ceiling(want.Width);
            probe.Window.Height = Math.Ceiling(want.Height);
            Dispatcher.UIThread.RunJobs();

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap? frame = probe.Window.GetLastRenderedFrame();
            frame.ShouldNotBeNull("the page rasterised no frame at all");

            Directory.CreateDirectory(OutputDirectory);
            string path = Path.Combine(OutputDirectory, $"{tabId}-{state}@{scaling:0.#}x.png");
            frame.Save(path, quality: null);

            frame.PixelSize.Width.ShouldBe((int)Math.Round(probe.Window.Width * scaling));
            DistinctColours(frame).ShouldBeGreaterThan(8,
                "a sheet with almost one colour in it is a page that drew nothing");
        });
    }

    [Fact]
    public void The_command_palette_rasterises_into_a_sheet_too()
    {
        session.On(() =>
        {
            var palette = new SpectraEngine.Editor.Shell.CommandPaletteView();
            palette.RowList.ItemsSource =
                CommandTable.Search("in", new CommandContext(
                    HasSelection: true, IsPlaying: false, HasSession: true,
                    HasProject: true, RibbonExpanded: true, CanPlay: true)).Rows;
            palette.RowList.SelectedIndex = 0;
            palette.QueryBox.Text = "in";

            var window = new Window { Content = palette, SizeToContent = SizeToContent.WidthAndHeight };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap? frame = window.GetLastRenderedFrame();
            frame.ShouldNotBeNull();

            Directory.CreateDirectory(OutputDirectory);
            frame.Save(Path.Combine(OutputDirectory, "palette@2x.png"), quality: null);

            DistinctColours(frame).ShouldBeGreaterThan(8);
            window.Close();
        });
    }

    [Fact]
    public void The_problems_panel_rasterises_into_a_sheet()
    {
        session.On(() =>
        {
            var model = new ShellModel();
            model.Problems.Report(
                SpectraEngine.Editor.Shell.OutputSeverity.Error,
                "Material {Path} is unreadable",
                "Materials/wall.spectramat could not be read.",
                "Materials/wall.spectramat");
            model.Problems.Report(
                SpectraEngine.Editor.Shell.OutputSeverity.Warning,
                "Map: a mesh node loaded without its model",
                "Signpost loaded without its model and draws nothing.",
                "Signpost");
            model.Problems.Report(
                SpectraEngine.Editor.Shell.OutputSeverity.Warning,
                "Map: a mesh node loaded without its model",
                "Signpost loaded without its model and draws nothing.",
                "Signpost");

            var panel = new SpectraEngine.Editor.Shell.ProblemsPanel { DataContext = model };
            var window = new Window { Content = panel, Width = 520, Height = 200 };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap? frame = window.GetLastRenderedFrame();
            frame.ShouldNotBeNull();

            Directory.CreateDirectory(OutputDirectory);
            frame.Save(Path.Combine(OutputDirectory, "problems@2x.png"), quality: null);

            DistinctColours(frame).ShouldBeGreaterThan(8);
            window.Close();
        });
    }

    [Fact]
    public void The_colour_picker_rasterises_into_a_sheet()
    {
        session.On(() =>
        {
            var picker = new SpectraEngine.Editor.Shell.ColorPickerView();
            picker.Open(SpectraEngine.Editor.Shell.ColorMath.SrgbToLinear(
                new System.Numerics.Vector3(0.85f, 0.35f, 0.15f)));

            var window = new Window { Content = picker, SizeToContent = SizeToContent.WidthAndHeight };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap? frame = window.GetLastRenderedFrame();
            frame.ShouldNotBeNull();

            Directory.CreateDirectory(OutputDirectory);
            frame.Save(Path.Combine(OutputDirectory, "picker@2x.png"), quality: null);

            // A gradient needs many colours. Eight would pass on flat rectangles.
            DistinctColours(frame).ShouldBeGreaterThan(64);
            window.Close();
        });
    }

    private static Action<ShellModel>? Drive(string state) => state switch
    {
        // Everything a page can light at once.
        "active" => m =>
        {
            m.RequestGizmoMode("rotate");
            m.RequestSnapEnabled(true);
            m.RequestGizmoStyle("Classic");
            m.RequestOrientation("local");
            foreach (DebugVisualization flag in new[]
                     {
                         DebugVisualization.Wireframe, DebugVisualization.Vertices,
                         DebugVisualization.Aabbs, DebugVisualization.Normals,
                         DebugVisualization.SceneGraph,
                     })
            {
                m.RequestDebugVisualization(flag, true);
            }
        },

        // A selection and entity classes, so nothing is greyed out. Fixture
        // classes: this process registers none, and Make entity needs one
        // that is made from geometry.
        "working" => m =>
        {
            m.ApplySnapshot(new FrameSnapshot { SelectedIds = [Guid.NewGuid()] });
            m.SetEntityClasses(EntityInsertMenu.Build(
                EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
                [
                    new EntitySchema("sheet_door", placement: EntityPlacement.Brush),
                    new EntitySchema("sheet_relay", placement: EntityPlacement.Abstract),
                ]))));
        },

        // Default model: a session that has just opened.
        _ => null,
    };

    private static int DistinctColours(WriteableBitmap frame)
    {
        using ILockedFramebuffer buf = frame.Lock();
        var seen = new HashSet<uint>();
        unsafe
        {
            for (int y = 0; y < buf.Size.Height; y++)
            {
                uint* row = (uint*)((byte*)buf.Address + (y * buf.RowBytes));
                for (int x = 0; x < buf.Size.Width; x++)
                    seen.Add(row[x]);
            }
        }

        return seen.Count;
    }

    private static string FindArtifacts()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Spectra.slnx")))
            dir = dir.Parent;

        return Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, "artifacts", "ribbon");
    }
    [Fact]
    public void The_viewport_header_rasterises_into_a_sheet()
    {
        session.On(() =>
        {
            var model = new SpectraEngine.Editor.Shell.ShellModel { HasSession = true };
            model.ApplySnapshot(new SpectraEngine.Core.Hosting.FrameSnapshot
            {
                ViewName = "Top",
                GridModeName = "auto",
                NavigationModeName = "editor freelook",
                PipelineNames = ["Deferred", "Forward", "Wireframe"],
                PipelineName = "Deferred",
                DebugFlags = SpectraEngine.Core.Scene.DebugVisualization.Wireframe,
                CameraPosition = new System.Numerics.Vector3(12.5f, 8f, -140.25f),
            });

            var strip = new SpectraEngine.Editor.Shell.ViewportHeaderStrip { DataContext = model };
            var window = new Window { Content = strip, Width = 700, Height = 28 };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap? frame = window.GetLastRenderedFrame();
            frame.ShouldNotBeNull();

            Directory.CreateDirectory(OutputDirectory);
            frame.Save(Path.Combine(OutputDirectory, "header@2x.png"), quality: null);

            // A blank sheet means the bindings failed, which Avalonia does not report.
            DistinctColours(frame).ShouldBeGreaterThan(8);
        });
    }
}
