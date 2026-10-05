namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>The sizes the Logic view's graph is laid out with, in pixels.</summary>
public static class LogicMetrics
{
    /// <summary>How wide every card is.</summary>
    public const double CardWidth = 184;

    /// <summary>A card's header: the name over the class.</summary>
    public const double HeaderHeight = 42;

    /// <summary>The row under the header that shows what an entity is doing while the level plays.</summary>
    public const double StateRowHeight = 20;

    /// <summary>One input or output.</summary>
    public const double PortRowHeight = 22;

    /// <summary>The line under the ports about the outputs a card does not list.</summary>
    public const double NoteRowHeight = 20;

    /// <summary>The room under a card's last row.</summary>
    public const double CardBottomPadding = 6;

    /// <summary>How tall a wire's label is.</summary>
    public const double LabelHeight = 18;

    /// <summary>The room on each side of a label's text.</summary>
    public const double LabelPadding = 9;

    /// <summary>The least room between two boxes that hold text.</summary>
    public const double TextGap = 6;

    /// <summary>The gap between two columns of cards, unless a label needs more.</summary>
    public const double LaneWidth = 108;

    /// <summary>The room a lane keeps between its widest label and the cards on each side.</summary>
    public const double LaneMargin = 12;

    /// <summary>The room between two cards in one column.</summary>
    public const double CardGap = 24;

    /// <summary>The room a wire takes where it passes through a column between cards.</summary>
    public const double SlotHeight = 8;

    /// <summary>The room between two wires that pass through a column side by side.</summary>
    public const double SlotGap = 6;

    /// <summary>The room between a card and a wire that passes it.</summary>
    public const double CardSlotGap = 12;

    /// <summary>How far under a card its first wire to itself runs.</summary>
    public const double LoopDrop = 26;

    /// <summary>The room each further wire under a card or under a group takes.</summary>
    public const double RunStep = LabelHeight + TextGap;

    /// <summary>How far a wire to the card itself swings out past the card's sides.</summary>
    public const double LoopReach = 24;

    /// <summary>How much further each next wire to the same card swings out.</summary>
    public const double LoopReachStep = 8;

    /// <summary>The furthest such a wire swings out. Past this it would leave the scene's padding.</summary>
    public const double LoopReachLimit = 40;

    /// <summary>How far under a group of cards its first wire back to an earlier column runs.</summary>
    public const double BackRunDrop = 20;

    /// <summary>The room between two such wires where they run down or up beside a column.</summary>
    public const double BackRunStep = 6;

    /// <summary>The radius of the corners such a wire turns.</summary>
    public const double CornerRadius = 6;

    /// <summary>The room between two groups of cards that share no wire.</summary>
    public const double GroupGap = 40;

    /// <summary>The room around the whole graph.</summary>
    public const double ScenePadding = 24;
}
