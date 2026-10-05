using Avalonia;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// What a card shows and how tall that makes it, before it has a place.
internal sealed class LogicCardFace
{
    private readonly List<LogicPort> _rows = [];

    public LogicCardFace(LogicCard card, LogicLayoutOptions options)
    {
        Card = card;
        IsExpanded = !card.IsStub && options.ExpandedCards.Contains(card.NodeId);
        HasStateRow = options.ShowsState && !card.IsStub;

        int declaredOutputs = 0;
        int wiredOutputs = 0;

        foreach (LogicPort port in card.Inputs)
        {
            if (port.IsWired || IsExpanded)
                _rows.Add(port);
        }

        foreach (LogicPort port in card.Outputs)
        {
            if (port.IsWired || IsExpanded)
                _rows.Add(port);

            declaredOutputs += port.IsDeclared ? 1 : 0;
            wiredOutputs += port.IsDeclared && port.IsWired ? 1 : 0;
        }

        Note = IsExpanded ? "" : LogicText.OutputsNote(declaredOutputs, wiredOutputs);
        Height = RowsTop
            + LogicMetrics.PortRowHeight * _rows.Count
            + (Note.Length > 0 ? LogicMetrics.NoteRowHeight : 0)
            + LogicMetrics.CardBottomPadding;
    }

    public LogicCard Card { get; }

    public bool IsExpanded { get; }

    public bool HasStateRow { get; }

    public string Note { get; }

    public double Height { get; }

    private double RowsTop =>
        LogicMetrics.HeaderHeight + (HasStateRow ? LogicMetrics.StateRowHeight : 0);

    // How far under the card's top a wire meets a port.
    public double AnchorOffset(string port, bool isOutput)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].IsOutput == isOutput && string.Equals(_rows[i].Name, port, StringComparison.Ordinal))
                return RowsTop + LogicMetrics.PortRowHeight * (i + 0.5);
        }

        // Not reached: every wired port has a row.
        return LogicMetrics.HeaderHeight / 2;
    }

    public LogicSceneCard Place(Point topLeft, bool isSelected, bool isDimmed)
    {
        double x = topLeft.X;
        double y = topLeft.Y;
        double width = LogicMetrics.CardWidth;
        var ports = new LogicScenePort[_rows.Count];

        for (int i = 0; i < ports.Length; i++)
        {
            double top = y + RowsTop + LogicMetrics.PortRowHeight * i;
            var anchor = new Point(_rows[i].IsOutput ? x + width : x, top + LogicMetrics.PortRowHeight / 2);
            ports[i] = new LogicScenePort(_rows[i], new Rect(x, top, width, LogicMetrics.PortRowHeight), anchor);
        }

        double noteTop = y + RowsTop + LogicMetrics.PortRowHeight * ports.Length;

        return new LogicSceneCard(Card, new Rect(x, y, width, Height), ports)
        {
            StateRow = HasStateRow
                ? new Rect(x, y + LogicMetrics.HeaderHeight, width, LogicMetrics.StateRowHeight)
                : null,
            Note = Note,
            NoteRow = Note.Length > 0 ? new Rect(x, noteTop, width, LogicMetrics.NoteRowHeight) : null,
            IsExpanded = IsExpanded,
            IsSelected = isSelected,
            IsDimmed = isDimmed,
        };
    }
}
