using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Hosting;

/// <summary>
/// What a wiring view asks the engine to publish. Sent with
/// <see cref="EngineHost.RequestLogicView"/>.
/// </summary>
public sealed class LogicViewRequest
{
    /// <summary>The request of a view that is not showing: publish nothing.</summary>
    public static LogicViewRequest Hidden { get; } = new(false, []);

    /// <summary>A showing view.</summary>
    /// <param name="stateNodes">
    /// The entities it wants a line of live state for while a level runs.
    /// Copied, so the caller may reuse its list.
    /// </param>
    public LogicViewRequest(IReadOnlyList<Guid> stateNodes)
        : this(true, Copy(stateNodes))
    {
    }

    private LogicViewRequest(bool isShown, Guid[] stateNodes)
    {
        IsShown = isShown;
        StateNodes = stateNodes;
    }

    /// <summary>Whether a view is showing.</summary>
    public bool IsShown { get; }

    /// <summary>The entities the view wants live state for.</summary>
    public IReadOnlyList<Guid> StateNodes { get; }

    private static Guid[] Copy(IReadOnlyList<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var copy = new Guid[ids.Count];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = ids[i];

        return copy;
    }
}
