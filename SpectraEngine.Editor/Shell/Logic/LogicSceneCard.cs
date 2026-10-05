using Avalonia;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>A card with a place in the scene.</summary>
public sealed class LogicSceneCard
{
    internal LogicSceneCard(LogicCard card, Rect bounds, IReadOnlyList<LogicScenePort> ports)
    {
        Card = card;
        Bounds = bounds;
        Ports = ports;
    }

    /// <summary>The card this draws.</summary>
    public LogicCard Card { get; }

    /// <summary>The whole card.</summary>
    public Rect Bounds { get; }

    /// <summary>The name over the class, at the top.</summary>
    public Rect Header => new(Bounds.X, Bounds.Y, Bounds.Width, LogicMetrics.HeaderHeight);

    /// <summary>The row under the header for what the entity is doing, when the layout was asked for one.</summary>
    public Rect? StateRow { get; internal init; }

    /// <summary>The port rows from the top: inputs, then outputs.</summary>
    public IReadOnlyList<LogicScenePort> Ports { get; }

    /// <summary>What the card says about outputs it does not list, or empty.</summary>
    public string Note { get; internal init; } = "";

    /// <summary>The row the note is drawn in, when there is one.</summary>
    public Rect? NoteRow { get; internal init; }

    /// <summary>Whether the card lists every port its class declares.</summary>
    public bool IsExpanded { get; internal init; }

    /// <summary>Whether the card's entity is selected.</summary>
    public bool IsSelected { get; internal init; }

    /// <summary>Whether the filter leaves the card out.</summary>
    public bool IsDimmed { get; internal init; }

    /// <summary>What the card stands for when it is not an entity.</summary>
    public LogicStubKind Stub => Card.Stub;

    /// <summary>The row of an input or output, or null when the card does not list it.</summary>
    public LogicScenePort? PortNamed(string name, bool isOutput)
    {
        foreach (LogicScenePort port in Ports)
        {
            if (port.IsOutput == isOutput && string.Equals(port.Name, name, StringComparison.Ordinal))
                return port;
        }

        return null;
    }
}
