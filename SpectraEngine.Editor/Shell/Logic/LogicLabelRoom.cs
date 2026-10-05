namespace SpectraEngine.Editor.Shell.Logic;

// The room a wire's label keeps while a level runs: wide enough for anything
// the wire may come to say, so nothing moves when it says it.
internal static class LogicLabelRoom
{
    // The same graph, each edge labelled with the widest thing its label may
    // have to hold. What is drawn there is decided wire by wire, later.
    public static LogicScopedGraph Reserve(LogicScopedGraph graph, int digits, ILogicTextMeasure ruler)
    {
        LogicLabel room = LogicLabel.None;
        double roomWidth = 0;

        foreach (string text in LogicWireState.LongestTexts(digits))
        {
            double width = ruler.Width(text, LogicTextStyle.Label);
            if (width > roomWidth)
                (room, roomWidth) = (new LogicLabel(text, false), width);
        }

        return graph.WithLabels(edge =>
        {
            LogicTextStyle style = edge.Label.IsMono ? LogicTextStyle.MonoLabel : LogicTextStyle.Label;
            bool isWider = !edge.Label.IsEmpty && ruler.Width(edge.Label.Text, style) >= roomWidth;
            return isWider ? edge.Label : room;
        });
    }
}
