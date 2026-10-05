using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;

using static SpectraEngine.Editor.Tests.Logic.LogicFixture;
using static SpectraEngine.Editor.Tests.Logic.LogicPlayFixture;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// Rasterises the Logic view so a person can look at it: the vault level
/// whole, near a selected relay, running, empty and from far out, each in a
/// wide pane and a narrow one.
/// </summary>
// No golden images. A sheet has to exist, be the size asked for and not be
// one flat colour. The rest is for eyes.
[Collection(RibbonSessionCollection.Name)]
public sealed class LogicSheetTests(RibbonSession session)
{
    private const double Scaling = 2.0;

    // The toolbar and the status row, which the graph does not get.
    private const double Rows = 62;

    /// <summary>Where the sheets land. Gitignored.</summary>
    public static string OutputDirectory { get; } =
        Path.Combine(Path.GetDirectoryName(RibbonSheetTests.OutputDirectory) ?? "", "logic");

    [Theory]
    [InlineData("whole", 1123, 880)]
    [InlineData("whole", 480, 500)]
    [InlineData("around-relay", 1123, 880)]
    [InlineData("around-relay", 480, 500)]
    [InlineData("playing", 1123, 880)]
    [InlineData("playing", 480, 500)]
    [InlineData("empty", 1123, 880)]
    [InlineData("empty", 480, 500)]
    [InlineData("far", 1123, 880)]
    [InlineData("far", 480, 500)]
    public void The_Logic_view_rasterises_into_a_sheet(string state, int width, int height)
    {
        session.On(() =>
        {
            (LogicView view, Window window) = Open(Drive(state, width, height), width, height);

            try
            {
                WriteableBitmap frame = Rasterise(window);
                Save(frame, $"{state}-{width}");

                frame.PixelSize.ShouldBe(new PixelSize((int)(width * Scaling), (int)(height * Scaling)));
                DistinctColours(frame).ShouldBeGreaterThan(8, "a sheet of one colour is a view that drew nothing");
                view.Bounds.Size.ShouldBe(new Size(width, height));
                LogicTheme.Missing.ShouldBeEmpty("the graph asked the theme for a key it does not have");
            }
            finally
            {
                window.Close();
            }
        });
    }

    // Not asked for by name. They show what the others cannot: text cut to
    // its card, what a filter dims, the cards between near and far, a level
    // with no wires, and the smallest pane.
    [Theory]
    [InlineData("long-names", 1123, 500)]
    [InlineData("filter", 1123, 880)]
    [InlineData("compact", 1123, 880)]
    [InlineData("playing-near", 900, 700)]
    [InlineData("no-wires", 480, 500)]
    [InlineData("playing", 180, 90)]
    public void More_of_the_Logic_view_rasterises_into_sheets(string state, int width, int height)
    {
        session.On(() =>
        {
            (_, Window window) = Open(Drive(state, width, height), width, height);

            try
            {
                WriteableBitmap frame = Rasterise(window);
                Save(frame, $"{state}-{width}");

                frame.PixelSize.ShouldBe(new PixelSize((int)(width * Scaling), (int)(height * Scaling)));
                LogicTheme.Missing.ShouldBeEmpty();
            }
            finally
            {
                window.Close();
            }
        });
    }

    internal static (LogicView View, Window Window) Open(LogicViewModel model, double width, double height)
    {
        var view = new LogicView { Model = model };
        var window = new Window { Content = view, Width = width, Height = height };

        window.SetRenderScaling(Scaling);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (view, window);
    }

    // Everything a sheet shows is decided here, before its window opens: a
    // change made after the first layout may miss the frame that is captured.
    internal static LogicViewModel Drive(string state, double width, double height)
    {
        var model = new LogicViewModel { Schemas = Catalog };
        LogicGraphInfo level = Level(VaultEntities());

        switch (state)
        {
            case "whole":
                model.Mode = LogicScopeMode.WholeLevel;
                model.Apply(Snapshot(level));
                break;

            case "around-relay":
                model.Apply(Snapshot(level, null, OpenVault));
                break;

            case "playing":
                model.Mode = LogicScopeMode.WholeLevel;
                model.Apply(Snapshot(level, VaultPlaying(), OpenVault));
                break;

            case "playing-near":
                model.Apply(Snapshot(level, VaultPlaying(), OpenVault));
                break;

            case "empty":
                model.Apply(Snapshot(level));
                break;

            case "no-wires":
                model.Apply(Snapshot(Level(Entity(1, "PlayerStart", "info_player_start"))));
                break;

            case "far":
                model.Mode = LogicScopeMode.WholeLevel;
                model.Apply(Snapshot(level, null, OpenVault));
                LookFrom(model, 0.2, new Size(width, height - Rows));
                break;

            case "compact":
                model.Mode = LogicScopeMode.WholeLevel;
                model.Apply(Snapshot(level, null, OpenVault));
                LookFrom(model, 0.35, new Size(width, height - Rows));
                break;

            case "filter":
                model.Mode = LogicScopeMode.WholeLevel;
                model.Filter = "door";
                model.Apply(Snapshot(level, null, OpenVault));
                break;

            case "long-names":
                model.Mode = LogicScopeMode.WholeLevel;
                model.Apply(Snapshot(LongNames(), LongPlaying()));
                break;
        }

        return model;
    }

    // Names, classes, ports, state and a parameter, each longer than a card
    // or a lane has room for.
    private static LogicGraphInfo LongNames() => Level(
        Entity(
            1, "TheButtonBesideTheVaultDoorOnTheUpperFloor", "func_button_with_a_class_name_nobody_registered",
            Wire("OnPressedForLongerThanAnyoneWouldReasonablyPress", "TheCounterThatCountsEveryPress", "Add", "a parameter with many words in it", 2.5f, 3)),
        Entity(
            2, "TheCounterThatCountsEveryPress", "math_counter",
            Wire("OnHitMax", "A door whose name is a whole sentence about itself", "OpenAsFarAsItWillGoAndStayThere")),
        Entity(3, "A door whose name is a whole sentence about itself", "func_door"));

    private static LogicPlayInfo LongPlaying() => new()
    {
        Tick = 1234567,
        Time = 40f,
        Wires = [Fired(Id(1), 0, 123456, 10)],
        States =
        [
            new(Id(1), "a label that goes on for longer than half a card", "and a value that does the same thing"),
            new(Id(2), "value", "1234567890 of 1234567890 and counting further"),
            new(Id(3), "", "a value with no label in front of it at all, long"),
        ],
    };

    // Puts the middle of the graph in the middle of a pane, at a zoom. The
    // model is told the pane's size first, so the fit it owes is spent here.
    private static void LookFrom(LogicViewModel model, double zoom, Size pane)
    {
        model.ViewSize = pane;
        model.View = new LogicPanZoom(default, zoom)
            .CenteredOn(new Rect(model.Scene.ShouldNotBeNull().Size), pane);
    }

    private static WriteableBitmap Rasterise(Window window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        return window.GetLastRenderedFrame().ShouldNotBeNull("the view rasterised no frame at all");
    }

    private static void Save(WriteableBitmap frame, string name)
    {
        Directory.CreateDirectory(OutputDirectory);
        frame.Save(Path.Combine(OutputDirectory, $"{name}@2x.png"), quality: null);
    }

    private static int DistinctColours(WriteableBitmap frame)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        var seen = new HashSet<uint>();

        unsafe
        {
            for (int y = 0; y < buffer.Size.Height; y++)
            {
                uint* row = (uint*)((byte*)buffer.Address + (y * buffer.RowBytes));
                for (int x = 0; x < buffer.Size.Width; x++)
                    seen.Add(row[x]);
            }
        }

        return seen.Count;
    }
}
