using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SpectraEngine.Editor.Shell;

/// <summary>The output log's view. Follows the tail only while already scrolled to it.</summary>
public partial class OutputPanel : UserControl
{
    private const double TailSlack = 4.0;

    public OutputPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
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

    private void OnAppended(OutputEntry entry)
    {
        if (Scroller is not { } scroller)
            return;

        // Measure before the new row is laid out. After it the extent has
        // grown and this is never at the tail.
        bool atTail = scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - TailSlack;
        if (!atTail)
            return;

        // Post: the new row is not measured yet, ScrollToEnd now lands short.
        Dispatcher.UIThread.Post(scroller.ScrollToEnd, DispatcherPriority.Background);
    }

    private void OnClearClicked(object? sender, RoutedEventArgs e) =>
        (DataContext as ShellModel)?.Output.Clear();
}
