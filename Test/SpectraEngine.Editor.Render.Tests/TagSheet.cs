using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Undo;
using SpectraEngine.Editor.Shell;
using System.Numerics;

namespace SpectraEngine.Editor.Render.Tests;

// The property panel over a real scene, at the inspector's expanded width
// and tall enough to show the Tags and Attributes sections with nothing
// scrolled off. What the panel posts is applied by the editors the engine
// uses, a gesture is one transaction, and keys and clicks go to the window.
internal sealed class TagSheet : IDisposable
{
    private readonly Scene _scene;
    private readonly SceneNode[] _selected;
    private readonly Window _window;
    private readonly ShellModel _shell;
    private TagPanelInfo? _tags;
    private AttributePanelInfo? _attributes;
    private bool _isPlaying;
    private bool _inGesture;
    private long _frame;

    public TagSheet(EntitySchemaCatalog? schemas, params SceneNode[] selected)
    {
        _scene = new Scene("sheet") { EntitySchemas = schemas };
        foreach (SceneNode node in selected)
            _scene.Root.AddChild(node);

        _selected = selected;
        Undo = new UndoStack(_scene);
        Panel = new PropertyPanelModel(
            _ => { }, BeginGesture, EndGesture, null,
            edit => TagEditor.Apply(Undo, _selected, edit),
            edit => AttributeEditor.Apply(Undo, _selected, edit, _inGesture))
        {
            Schemas = schemas,
        };

        _shell = new ShellModel { Properties = Panel };
        View = new PropertiesPanel { DataContext = _shell };
        _window = new Window { Content = View, Width = PropertyLabelFixture.ExpandedWidth, Height = 1180 };
        _window.SetRenderScaling(2.0);
        _window.Show();
        Publish();
    }

    public PropertyPanelModel Panel { get; }

    public UndoStack Undo { get; }

    public PropertiesPanel View { get; }

    // The two sections, with the handlers under test.
    public TagsAndAttributesView Sections => View.TagsAndAttributes;

    // A part with three tags and an attribute of every kind.
    public static SceneNode Brick() => new("Brick")
    {
        Tags = TagSet.Of("Lava", "Hot stuff", "Checkpoint 3"),
        Attributes = AttributeSet.Empty
            .With("Label", MemberValue.Of("hot"))
            .With("Damage", MemberValue.Of(25d))
            .With("Deadly", MemberValue.Of(true))
            .With("Push", MemberValue.Of(new Vector3(0f, 12.5f, -1f)))
            .With("Glow", MemberValue.Of(new Color3(1f, 0.5f, 0f)))
            .With("Rest", MemberValue.Of(new CFrame(
                new Vector3(1f, 2f, 3f), EulerAngles.FromDegrees(new Vector3(0f, 90f, 0f)).ToQuaternion()))),
    };

    // Hands the shell a snapshot as the engine would publish it now.
    public void Publish()
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(_selected, rows, _scene.EntitySchemas);
        _tags = TagPanelInfo.Capture(_selected, _tags);
        _attributes = AttributePanelInfo.Capture(_selected, _scene.EntitySchemas, _attributes);

        _shell.ApplySnapshot(new FrameSnapshot
        {
            FrameNumber = ++_frame,
            SelectedIds = [.. _selected.Select(node => node.Id)],
            SelectionProperties = rows,
            SelectionTags = _tags,
            SelectionAttributes = _attributes,
            IsPlaying = _isPlaying,
        });
        Dispatcher.UIThread.RunJobs();
    }

    // Starts or stops the level, as far as the shell is told.
    public void Play(bool isPlaying)
    {
        _isPlaying = isPlaying;

        // The shell holds its own word for a few snapshots before the engine's wins.
        for (int i = 0; i < 12; i++)
            Publish();
    }

    // Writes through the model, with no box and no key. For what a sheet shows.
    public void Type(PropertyFieldModel field, string text)
    {
        field.BeginEdit();
        field.Text = text;
        field.Commit();
        Publish();
    }

    // The box a field is typed into.
    public TextBox Box(PropertyFieldModel field) =>
        Shown<TextBox>().Single(box => ReferenceEquals(box.DataContext, field));

    // Puts the caret into a field's box with all of its text marked, so
    // what is typed next takes its place.
    public void Enter(PropertyFieldModel field)
    {
        TextBox box = Box(field);
        box.Focus().ShouldBeTrue();
        box.SelectAll();
        Dispatcher.UIThread.RunJobs();
    }

    public void Keys(string text)
    {
        _window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    public void Press(PhysicalKey key)
    {
        _window.KeyPressQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    // A press and a release of the left button in the middle of a control.
    public void Click(Control control)
    {
        Point at = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), _window).ShouldNotBeNull();

        _window.MouseDown(at, MouseButton.Left);
        _window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    public T[] Shown<T>() where T : Control =>
        [.. View.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible)];

    public void Save(string name)
    {
        // More than one tick: the frame of the first can be the one from
        // before the last change was handed to the renderer.
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }

        WriteableBitmap frame = _window.CaptureRenderedFrame().ShouldNotBeNull();
        frame.PixelSize.Width.ShouldBe((int)(PropertyLabelFixture.ExpandedWidth * 2));

        Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
        frame.Save(Path.Combine(RibbonSheetTests.OutputDirectory, name), new PngBitmapEncoderOptions());
    }

    public void Dispose() => _window.Close();

    private void BeginGesture(string name)
    {
        Undo.BeginTransaction(name);
        _inGesture = true;
    }

    private void EndGesture(bool commit)
    {
        if (!_inGesture)
            return;

        _inGesture = false;
        if (commit)
            Undo.CommitTransaction();
        else
            Undo.CancelTransaction();
    }
}
