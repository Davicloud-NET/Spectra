namespace SpectraEngine.Editor.Shell.Logic;

// The room a wire's label keeps while a level runs: wide enough for anything
// the wire may come to say, so nothing moves when it says it.
internal static class LogicLabelRoom
{
    // The same graph, each edge labelled with the widest thing its label may
    // have to hold. What is drawn there is decided wire by wire, later.
    public static LogicScopedGraph Reserve(LogicScopedGraph graph, ILogicTextMeasure ruler)
    {
        LogicLabel room = Widest(ruler, out double roomWidth);

        return graph.WithLabels(edge =>
        {
            LogicTextStyle style = edge.Label.IsMono ? LogicTextStyle.MonoLabel : LogicTextStyle.Label;
            bool isWider = !edge.Label.IsEmpty && ruler.Width(edge.Label.Text, style) >= roomWidth;
            return isWider ? edge.Label : room;
        });
    }

    // Digits differ in width in a proportional font, so each is tried.
    private static LogicLabel Widest(ILogicTextMeasure ruler, out double widest)
    {
        LogicLabel room = LogicLabel.None;
        widest = 0;

        for (char digit = '0'; digit <= '9'; digit++)
        {
            foreach (string text in LogicWireState.LongestTexts(digit))
            {
                double width = ruler.Width(text, LogicTextStyle.Label);
                if (width > widest)
                    (room, widest) = (new LogicLabel(text, false), width);
            }
        }

        return room;
    }
}
