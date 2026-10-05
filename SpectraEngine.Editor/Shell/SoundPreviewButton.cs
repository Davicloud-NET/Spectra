using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.ComponentModel;
using Path = Avalonia.Controls.Shapes.Path;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The small play button beside a sound file. It shows stop while the engine
/// says that file is the one playing.
/// </summary>
// A border and one glyph, not a Button with a template: a grid of tiles
// builds one of these per tile, and a hidden one builds nothing at all. Its
// looks are in Theme/Controls.axaml.
public sealed class SoundPreviewButton : Border
{
    /// <summary>
    /// Defines the model every button below a control talks to. Inherited, so
    /// a panel sets it once and the buttons in its rows and popups find it.
    /// </summary>
    public static readonly AttachedProperty<SoundPreviewModel?> PreviewProperty =
        AvaloniaProperty.RegisterAttached<SoundPreviewButton, Control, SoundPreviewModel?>(
            "Preview", inherits: true);

    /// <summary>Defines <see cref="ContentPath"/>.</summary>
    public static readonly StyledProperty<string?> ContentPathProperty =
        AvaloniaProperty.Register<SoundPreviewButton, string?>(nameof(ContentPath));

    /// <summary>What the button says while its file is not playing.</summary>
    public const string PlayTip = "Listen to this sound";

    /// <summary>What the button says while its file is playing.</summary>
    public const string StopTip = "Stop";

    private const string PlayClass = "playglyph";
    private const string StopClass = "stopglyph";

    // Listened to only while the button is on screen and shown, so a row a
    // list has thrown away is not kept alive by the model.
    private SoundPreviewModel? _heard;
    private Path? _glyph;
    private bool _isAttached;
    private bool _isPressed;

    /// <summary>Creates the button.</summary>
    public SoundPreviewButton()
    {
        // A second click on the button must not reach the row under it, where
        // a double click picks or inserts the file.
        AddHandler(DoubleTappedEvent, static (_, e) => e.Handled = true);
    }

    /// <summary>The sound file this button plays, as a content path.</summary>
    public string? ContentPath
    {
        get => GetValue(ContentPathProperty);
        set => SetValue(ContentPathProperty, value);
    }

    /// <summary>Whether the button shows stop.</summary>
    public bool ShowsStop { get; private set; }

    /// <summary>Reads the model a control's buttons talk to.</summary>
    public static SoundPreviewModel? GetPreview(Control control) => control.GetValue(PreviewProperty);

    /// <summary>Sets the model a control's buttons talk to.</summary>
    public static void SetPreview(Control control, SoundPreviewModel? value) =>
        control.SetValue(PreviewProperty, value);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PreviewProperty
            || change.Property == ContentPathProperty
            || change.Property == IsVisibleProperty)
        {
            Hear();
        }
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _isAttached = true;
        Hear();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        Hear();

        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // Handled, or the row under the button takes the press as its own.
        SetPressed(true);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_isPressed || e.InitialPressMouseButton != MouseButton.Left)
            return;

        SetPressed(false);
        e.Handled = true;

        // A press dragged off the button and let go there is no press.
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this)))
            _heard?.Press(ContentPath);
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        SetPressed(false);
    }

    private void SetPressed(bool pressed)
    {
        _isPressed = pressed;
        PseudoClasses.Set(":pressed", pressed);
    }

    private void Hear()
    {
        SoundPreviewModel? model = _isAttached && IsVisible ? GetValue(PreviewProperty) : null;

        if (!ReferenceEquals(_heard, model))
        {
            if (_heard is not null)
                _heard.PropertyChanged -= OnPreviewChanged;

            _heard = model;

            if (model is not null)
                model.PropertyChanged += OnPreviewChanged;
        }

        Show(model?.IsPlaying(ContentPath) ?? false);
    }

    private void OnPreviewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SoundPreviewModel.Playing))
            Show(_heard?.IsPlaying(ContentPath) ?? false);
    }

    private void Show(bool playing)
    {
        ShowsStop = playing;

        // A button nobody sees needs no glyph.
        if (_glyph is null && !(_isAttached && IsVisible))
            return;

        _glyph ??= NewGlyph();
        _glyph.Classes.Set(PlayClass, !playing);
        _glyph.Classes.Set(StopClass, playing);
        _glyph.Data = Icon(playing ? "IconStop" : "IconPlay");
        ToolTip.SetTip(this, playing ? StopTip : PlayTip);
    }

    private Path NewGlyph()
    {
        // Filled, where the other icons are stroked. The border takes the
        // pointer, so a press on the ink and one beside it are the same press.
        var glyph = new Path { StrokeThickness = 0, IsHitTestVisible = false };
        glyph.Classes.Add("icon");
        Child = glyph;
        return glyph;
    }

    private static Geometry? Icon(string key) =>
        Application.Current?.TryFindResource(key, out object? value) == true ? value as Geometry : null;
}
