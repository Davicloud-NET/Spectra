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
/// Rasterises both pages to PNG so a person can look at them.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the weak half of the suite and it says so.</b> Nothing here
/// judges how the surface LOOKS - not colour, not contrast, not whether the
/// hierarchy reads. Everything it asserts is that a frame really rasterised at
/// the size layout promised and is not one flat colour, which is what a page
/// that threw inside Render, or drew nothing, produces.
/// </para>
/// <para>
/// <b>Golden images are refused, deliberately.</b> This surface carries five
/// gradients, seven brush transitions and a <c>DoubleTransition</c> on
/// <c>Border.sheen</c>, so a frame captured one tick early differs from one
/// captured a tick late by a few per cent of alpha - a vague twitch rather than
/// anything nameable, which is precisely the diff nobody can act on.
/// </para>
/// </remarks>
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

            // Size the window to what the page actually wants, so the sheet is the
            // surface rather than the surface adrift in a 2600px field.
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

    /// <summary>Puts the model into the state the sheet is named for.</summary>
    private static Action<ShellModel>? Drive(string state) => state switch
    {
        // Everything a page can light at once, plus a selection so the Arrange
        // group and Frame are enabled.
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

        // THE STATE THE SURFACE IS ACTUALLY USED IN: something selected and a
        // project that declares entity classes, so nothing is greyed. A sheet of
        // an all-disabled page exaggerates how dead the surface looks and hides
        // whether the enabled one reads.
        "working" => m =>
        {
            m.ApplySnapshot(new FrameSnapshot { SelectedIds = [Guid.NewGuid()] });
            m.SetEntityClasses(EntityInsertMenu.Build(
                EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(EntityCatalog.Shared.Schemas))));
        },

        // The default model: no selection and no entity classes, which is what
        // a session looks like the moment it opens.
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
}
