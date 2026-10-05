using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.ComponentModel;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The small play button beside a sound file. It shows stop while the engine
/// says that file is the one playing.
/// </summary>
// Don't hand-write InitializeComponent: a parameterless one shadows the
// generated overload and every x:Name field stays null.
public partial class SoundPreviewButton : UserControl
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

    // Listened to only while the button is on screen, so a row a list has
    // thrown away is not kept alive by the model.
    private SoundPreviewModel? _heard;
    private bool _isAttached;

    /// <summary>Creates the button.</summary>
    public SoundPreviewButton()
    {
        InitializeComponent();

        // A second click on the button must not reach the row under it, where
        // a double click picks or inserts the file.
        AddHandler(DoubleTappedEvent, static (_, e) => e.Handled = true);

        Show(false);
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

        if (change.Property == PreviewProperty || change.Property == ContentPathProperty)
            Hear(_isAttached ? GetValue(PreviewProperty) : null);
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _isAttached = true;
        Hear(GetValue(PreviewProperty));
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        Hear(null);

        base.OnDetachedFromVisualTree(e);
    }

    private void Hear(SoundPreviewModel? model)
    {
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

    private void OnPreviewChanged(object? sender, PropertyChangedEventArgs e) =>
        Show(_heard?.IsPlaying(ContentPath) ?? false);

    private void Show(bool playing)
    {
        ShowsStop = playing;
        PlayGlyph.IsVisible = !playing;
        StopGlyph.IsVisible = playing;
        ToolTip.SetTip(ButtonPart, playing ? StopTip : PlayTip);
    }

    private void OnPressed(object? sender, RoutedEventArgs e)
    {
        _heard?.Press(ContentPath);
        e.Handled = true;
    }
}
