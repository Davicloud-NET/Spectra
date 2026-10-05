namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>One wire as the view draws it now: where it runs, how it looks and what its label says.</summary>
public sealed class LogicWireFace
{
    private readonly LogicEdge _authored;
    private string _stateText = "";
    private string? _sentence;

    internal LogicWireFace(LogicSceneEdge edge, LogicEdge authored)
    {
        Edge = edge;
        _authored = authored;
    }

    /// <summary>
    /// The wire's path and the room kept for its label. While a level runs
    /// the room is wider than the authored label needs.
    /// </summary>
    public LogicSceneEdge Edge { get; }

    /// <summary>The label the level was authored with. Empty for none.</summary>
    public LogicLabel Authored => _authored.Label;

    /// <summary>How the wire looks right now.</summary>
    public LogicWireState State { get; private set; }

    /// <summary>What the label says: what the wire is doing, otherwise what it was authored with.</summary>
    public string Text => State.HasText ? _stateText : Authored.Text;

    /// <summary>Whether <see cref="Text"/> is drawn in the mono font.</summary>
    public bool IsMono => !State.HasText && Authored.IsMono;

    /// <summary>The wire in words, for a tooltip.</summary>
    public string Sentence => _sentence ??= LogicViewText.Sentence(_authored);

    // Returns whether a drawing of the wire would differ.
    internal bool Take(LogicWireState state)
    {
        if (state == State)
            return false;

        // The words are only written again when they would read differently.
        if (!state.SaysTheSameAs(State))
            _stateText = state.Text;

        State = state;
        return true;
    }
}
