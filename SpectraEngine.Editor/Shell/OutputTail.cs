using Avalonia.Controls;

namespace SpectraEngine.Editor.Shell;

// Whether a view of the output shows its newest line. A view that does keeps
// doing so when a line comes, and one somebody scrolled up stays where it is.
internal static class OutputTail
{
    private const double Slack = 4.0;

    // Asked before the new row is laid out. After it the extent has grown
    // and no view is at its end.
    public static bool IsShown(ScrollViewer? scroller) =>
        scroller is not null
        && scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - Slack;
}
