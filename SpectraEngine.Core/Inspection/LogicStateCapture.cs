using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Inspection;

// Reads one line of live state from each entity a wiring view asked about.
internal static class LogicStateCapture
{
    public static LogicEntityState[] Capture(
        EntityWorld world,
        IReadOnlyList<Guid> nodes,
        EntityHeadline headline,
        List<KeyValuePair<string, string>> rows)
    {
        if (world.Index is not { } index || nodes.Count == 0)
            return [];

        var states = new List<LogicEntityState>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!index.TryGetByNodeId(nodes[i], out Entity? entity) || entity is null)
                continue;

            if (TryRead(entity, headline, rows, out string label, out string value))
                states.Add(new LogicEntityState(nodes[i], label, value));
        }

        return [.. states];
    }

    // The headline when the class gives one, otherwise its first row. False
    // for an entity with no state at all.
    public static bool TryRead(
        Entity entity,
        EntityHeadline headline,
        List<KeyValuePair<string, string>> rows,
        out string label,
        out string value)
    {
        headline.Clear();
        entity.DescribeState(new EntityStateWriter(headline));

        if (headline.IsSet)
        {
            label = headline.Label;
            value = headline.Value;
            return true;
        }

        rows.Clear();
        entity.DescribeState(new EntityStateWriter(rows));

        if (rows.Count == 0)
        {
            label = "";
            value = "";
            return false;
        }

        label = rows[0].Key;
        value = rows[0].Value;
        return true;
    }
}
