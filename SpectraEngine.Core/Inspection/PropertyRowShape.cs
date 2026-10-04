using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// The identity of a published row list: which properties they are and in what
/// order, with every value ignored. A panel rebuilds its controls only when this changes.
/// </summary>
// Capture from the published rows, not from the controls built: a panel does
// not build one control per row, so those two never match.
public sealed class PropertyRowShape
{
    private readonly List<(PropertyId Id, string Key)> _entries = [];

    /// <summary>How many rows this shape was captured from.</summary>
    public int Count => _entries.Count;

    /// <summary>Forgets the captured shape.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>Appends one row's identity.</summary>
    // Key can be null on a default-constructed row.
    public void Add(in PropertyRow row) => _entries.Add((row.Id, row.Key ?? ""));

    /// <summary>Replaces the captured shape with <paramref name="rows"/>'.</summary>
    public void CaptureFrom(IReadOnlyList<PropertyRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        _entries.Clear();
        for (int i = 0; i < rows.Count; i++)
            Add(rows[i]);
    }

    /// <summary>
    /// Whether <paramref name="rows"/> are the same properties, in the same
    /// order, as the captured shape.
    /// </summary>
    public bool Matches(IReadOnlyList<PropertyRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (_entries.Count != rows.Count)
            return false;

        for (int i = 0; i < rows.Count; i++)
        {
            (PropertyId Id, string Key) entry = _entries[i];
            if (entry.Id != rows[i].Id)
                return false;

            // Every entity keyvalue shares one id. Without the key, two classes
            // with equally long schemas match and values land in the wrong boxes.
            if (!string.Equals(entry.Key, rows[i].Key ?? "", StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
