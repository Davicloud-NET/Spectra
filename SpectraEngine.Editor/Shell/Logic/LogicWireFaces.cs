using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// One face for each of a scene's edges, in the scene's order.
internal sealed class LogicWireFaces
{
    private readonly Dictionary<LogicSceneEdge, LogicWireFace> _byEdge = [];
    private LogicWireFace[] _faces = [];

    public IReadOnlyList<LogicWireFace> All => _faces;

    public LogicWireFace? Of(LogicSceneEdge edge) => _byEdge.GetValueOrDefault(edge);

    public void Clear()
    {
        _byEdge.Clear();
        _faces = [];
    }

    // The scene was laid out from drawn, which is authored with other labels,
    // edge for edge.
    public void Rebuild(LogicScene scene, LogicScopedGraph drawn, LogicScopedGraph authored)
    {
        var authoredOf = new Dictionary<LogicEdge, LogicEdge>(drawn.Edges.Count);
        for (int i = 0; i < drawn.Edges.Count; i++)
            authoredOf[drawn.Edges[i]] = authored.Edges[i];

        _byEdge.Clear();
        _faces = new LogicWireFace[scene.Edges.Count];

        for (int i = 0; i < _faces.Length; i++)
        {
            LogicSceneEdge edge = scene.Edges[i];
            LogicEdge source = authoredOf[edge.Edge];

            _faces[i] = new LogicWireFace(edge, source, ShowsRefusals(authored.Graph, source));
            _byEdge[edge] = _faces[i];
        }
    }

    // Returns whether any wire would be drawn differently. Runs on every
    // snapshot of a running level.
    public bool Refresh(LogicPlayState play, LogicSelection selection)
    {
        bool changed = false;

        foreach (LogicWireFace face in _faces)
        {
            LogicEdge edge = face.Edge.Edge;
            LogicWireState state = LogicWireState.Authored(
                edge.GoesNowhere,
                selection.Contains(edge.From) || selection.Contains(edge.To));

            if (play.IsPlaying)
            {
                state = LogicWireState.Playing(
                    play.Activity(edge), face.ShowsRefusals, play.Tick, play.Time, state);
            }

            changed |= face.Take(state);
        }

        return changed;
    }

    // The engine counts refusals by wire, and a wire that reaches several
    // entities has an edge to each. The edges the schemas say lack the input
    // show the count. When they name none, every edge does, so a refusal
    // nobody foresaw still shows.
    private static bool ShowsRefusals(LogicGraph graph, LogicEdge edge)
    {
        if (edge.Verdict == LogicVerdict.NoSuchInput)
            return true;

        foreach (LogicEdge other in graph.Leaving(edge.From))
        {
            if (other.Verdict == LogicVerdict.NoSuchInput && SharesWire(other, edge))
                return false;
        }

        return true;
    }

    private static bool SharesWire(LogicEdge a, LogicEdge b)
    {
        foreach (int wire in a.WireIndices)
        {
            foreach (int other in b.WireIndices)
            {
                if (wire == other)
                    return true;
            }
        }

        return false;
    }
}
