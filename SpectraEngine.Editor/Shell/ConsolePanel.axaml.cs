using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The console's view: one input line, and the shared output above it.
/// </summary>
public partial class ConsolePanel : UserControl
{
    private const int MaxHistory = 64;

    private readonly List<string> _history = [];
    private int _historyIndex;

    public ConsolePanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();

        // A line that opens something takes the keyboard when it is clicked.
        // In a console the next key is typed, so the input line gets it back.
        Lines.AddHandler(
            PointerReleasedEvent, (_, _) => Input.Focus(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Raised with a line the user submitted. The window resolves it.</summary>
    public event Action<string>? CommandSubmitted;

    /// <summary>Puts the caret in the input line and shows the newest line.</summary>
    public void FocusInput()
    {
        Input.Focus();
        ShowNewest();
    }

    // Comes up at the newest line, also when lines came while it was away.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ShowNewest();
    }

    private OutputLog? _log;

    private void Subscribe()
    {
        if (_log is not null)
            _log.Appended -= OnAppended;

        _log = (DataContext as ShellModel)?.Output;

        if (_log is not null)
            _log.Appended += OnAppended;
    }

    // Follows the newest line while it shows it, as the Output pane does, so
    // a click on an older line keeps that line in view. What somebody typed
    // brings the end back: the answer comes there.
    private void OnAppended(OutputEntry entry)
    {
        if (entry.IsCommand || OutputTail.IsShown(Scroller))
            ShowNewest();
    }

    // Post: a new row is not measured yet, and ScrollToEnd now lands short.
    private void ShowNewest() =>
        Dispatcher.UIThread.Post(() => Scroller?.ScrollToEnd(), DispatcherPriority.Background);

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Submit();
                e.Handled = true;
                break;

            case Key.Up:
                Recall(-1);
                e.Handled = true;
                break;

            case Key.Down:
                Recall(1);
                e.Handled = true;
                break;

            case Key.Escape:
                Input.Text = string.Empty;
                e.Handled = true;
                break;
        }
    }

    private void Submit()
    {
        string line = Input.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line))
            return;

        // Recorded before it runs, so a failed command can still be recalled.
        _history.Remove(line);
        _history.Add(line);
        if (_history.Count > MaxHistory)
            _history.RemoveAt(0);

        _historyIndex = _history.Count;

        Input.Text = string.Empty;
        CommandSubmitted?.Invoke(line);
    }

    private void Recall(int direction)
    {
        if (_history.Count == 0)
            return;

        int index = Math.Clamp(_historyIndex + direction, 0, _history.Count);
        _historyIndex = index;

        // Past the newest entry is a blank line.
        Input.Text = index >= _history.Count ? string.Empty : _history[index];
        Input.CaretIndex = Input.Text.Length;
    }
}
