using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;

using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The Scene panel's filter box in a level too long for the panel: typing
/// brings the first match onto the screen, and Enter, Down and Up select
/// matches without taking the keyboard out of the box.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class ScenePanelFilterTests(RibbonSession session)
{
    private const int NodeCount = 265;
    private const int HumIndex = 200;

    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid Hum = Guid.NewGuid();
    private static readonly Guid Machines = Guid.NewGuid();
    private static readonly Guid PumpA = Guid.NewGuid();
    private static readonly Guid Fan = Guid.NewGuid();
    private static readonly Guid PumpB = Guid.NewGuid();

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void Typing_a_name_scrolls_its_row_onto_the_screen()
    {
        On(panel =>
        {
            panel.Container(Hum).ShouldBeNull("the row starts out below the panel, with no container");

            panel.Type("Hum");
            panel.WaitForTheFilter();

            panel.Model.TreeCountLabel.ShouldBe($"1 / {NodeCount}");
            panel.ShouldShow(Hum);
            panel.Filter.IsFocused.ShouldBeTrue();
            panel.Requested.ShouldBeEmpty("typing alone selects nothing");

            panel.Save("scene-filter-typed@2x.png");
        });
    }

    [Fact]
    public void A_match_inside_a_closed_group_is_opened_and_scrolled_to()
    {
        On(panel =>
        {
            panel.Type("Fan");
            panel.WaitForTheFilter();

            panel.ShouldShow(Fan);
        });
    }

    [Fact]
    public void A_match_already_on_the_screen_does_not_move_the_list()
    {
        On(panel =>
        {
            panel.Type("Block 010");
            panel.WaitForTheFilter();

            panel.List.Scroll.ShouldNotBeNull().Offset.Y.ShouldBe(0);
        });
    }

    [Fact]
    public void Enter_selects_the_first_match_and_the_keyboard_stays_in_the_box()
    {
        On(panel =>
        {
            panel.Type("Hum");

            // Straight away, before the pause that applies the filter is over.
            panel.Press(PhysicalKey.Enter);

            panel.Requested.ShouldBe([[Hum]]);
            panel.Filter.IsFocused.ShouldBeTrue();
            panel.ShouldShow(Hum);
        });
    }

    [Fact]
    public void Down_and_Up_step_through_the_matches()
    {
        On(panel =>
        {
            panel.Type("Pump");
            panel.WaitForTheFilter();

            panel.Press(PhysicalKey.ArrowDown);
            panel.Press(PhysicalKey.ArrowDown);
            panel.Press(PhysicalKey.ArrowDown);
            panel.Press(PhysicalKey.ArrowUp);

            panel.Requested.ShouldBe([[PumpA], [PumpB], [PumpA], [PumpB]]);
            panel.Filter.IsFocused.ShouldBeTrue();
            panel.Filter.Text.ShouldBe("Pump");
        });
    }

    [Fact]
    public void Enter_with_no_match_selects_nothing()
    {
        On(panel =>
        {
            panel.Type("Nothing is called this");

            panel.Press(PhysicalKey.Enter);
            panel.Press(PhysicalKey.ArrowDown);

            panel.Requested.ShouldBeEmpty();
            panel.Model.HasNoMatches.ShouldBeTrue();
        });
    }

    [Fact]
    public void Escape_still_clears_the_filter()
    {
        On(panel =>
        {
            panel.Type("Hum");
            panel.WaitForTheFilter();

            panel.Press(PhysicalKey.Escape);

            panel.Filter.Text.ShouldBeNullOrEmpty();
            panel.Model.HasFilter.ShouldBeFalse();
            panel.Model.TreeCountLabel.ShouldBe($"{NodeCount}");
        });
    }

    private void On(Action<OpenPanel> body) => session.On(() =>
    {
        using var panel = new OpenPanel();
        body(panel);
    });

    // A root with 259 blocks, one node called Hum far down among them, and a
    // closed group at the end.
    private static SceneTreeModel Level()
    {
        var changes = new List<SceneChange> { Added(Root, Guid.Empty, "Level", -1, SceneNodeKind.Group) };

        int blocks = NodeCount - 6;
        for (int i = 0; i < blocks; i++)
            changes.Add(Added(Guid.NewGuid(), Root, $"Block {i:000}", -1, SceneNodeKind.BrushWorld));

        changes.Add(Added(Hum, Root, "Hum", HumIndex, SceneNodeKind.Entity));
        changes.Add(Added(Machines, Root, "Machines", -1, SceneNodeKind.Group));
        changes.Add(Added(PumpA, Machines, "Pump A", -1, SceneNodeKind.Mesh));
        changes.Add(Added(Fan, Machines, "Fan", -1, SceneNodeKind.Mesh));
        changes.Add(Added(PumpB, Machines, "Pump B", -1, SceneNodeKind.Mesh));

        var tree = new SceneTreeModel(new EngineHost(NullLogger.Instance), NullLogger.Instance);
        tree.ApplyChanges(new FrameSnapshot { FrameNumber = 1, Changes = changes });
        tree.Count.ShouldBe(NodeCount);
        return tree;
    }

    private static SceneChange Added(Guid id, Guid parent, string name, int index, SceneNodeKind kind) =>
        new(SceneChangeKind.Added, id, parent, name, index, kind);

    // The panel in a window the size of a docked Scene tool, with the filter
    // box focused.
    private sealed class OpenPanel : IDisposable
    {
        private readonly Window _window;
        private readonly ScenePanel _panel;
        private readonly SceneTreeModel _tree = Level();

        public OpenPanel()
        {
            Model = new ShellModel { Tree = _tree };
            Model.ApplySnapshot(new FrameSnapshot());

            _panel = new ScenePanel { DataContext = Model };
            _panel.SelectionRequested += ids => Requested.Add([.. ids]);

            _window = new Window { Content = _panel, Width = 320, Height = 400 };
            _window.SetRenderScaling(2.0);
            _window.Show();
            Filter.Focus();
            Dispatcher.UIThread.RunJobs();
        }

        public ShellModel Model { get; }

        public TextBox Filter => _panel.FilterBox;

        public ListBox List => _panel.SceneTree;

        public List<Guid[]> Requested { get; } = [];

        public void Type(string text) => _window.KeyTextInput(text);

        public void Press(PhysicalKey key)
        {
            _window.KeyPressQwerty(key, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        // The filter is applied by a dispatcher timer once typing pauses, so
        // the dispatcher has to run in real time until it has fired.
        public void WaitForTheFilter()
        {
            var frame = new DispatcherFrame();
            var watch = Stopwatch.StartNew();
            var poll = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background, (_, _) =>
            {
                if (Model.MatchCount != NodeCount || watch.Elapsed > Patience)
                    frame.Continue = false;
            });

            poll.Start();
            Dispatcher.UIThread.PushFrame(frame);
            poll.Stop();

            Model.MatchCount.ShouldNotBe(NodeCount, "the filter was never applied");
            Dispatcher.UIThread.RunJobs();
        }

        public Control? Container(Guid id)
        {
            _tree.TryGetNode(id, out SceneTreeNode node).ShouldBeTrue();
            return List.ContainerFromItem(node);
        }

        public void ShouldShow(Guid id)
        {
            Control row = Container(id).ShouldNotBeNull("the match has no row container");
            Rect inList = RibbonProbe.BoundsIn(row, List);

            inList.Top.ShouldBeGreaterThanOrEqualTo(0);
            inList.Bottom.ShouldBeLessThanOrEqualTo(List.Bounds.Height);
        }

        public void Save(string fileName)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap frame = _window.GetLastRenderedFrame().ShouldNotBeNull();

            Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
            frame.Save(Path.Combine(RibbonSheetTests.OutputDirectory, fileName), new PngBitmapEncoderOptions());
        }

        public void Dispose() => _window.Close();
    }
}
