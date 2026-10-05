using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Collects an entity's live state as named values for a person to read.
/// Numbers are written the same on every machine. Nothing parses the names.
/// </summary>
public readonly struct EntityStateWriter
{
    private readonly List<KeyValuePair<string, string>> _rows;

    /// <param name="rows">Gets one name and value per <c>Add</c>, in the order they are made.</param>
    public EntityStateWriter(List<KeyValuePair<string, string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows;
    }

    /// <summary>Adds a flag, as <c>1</c> or <c>0</c>.</summary>
    public void Add(string name, bool value) => Add(name, KeyvalueWire.Format(value));

    /// <summary>Adds a whole number.</summary>
    public void Add(string name, int value) => Add(name, KeyvalueWire.Format(value));

    /// <summary>Adds a number. Infinity and NaN are written out, not refused.</summary>
    // Not KeyvalueWire.Format: it throws on a float a map could not hold, and
    // a broken value is what someone reading state is looking for.
    public void Add(string name, float value) => Add(name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Adds text as it is.</summary>
    public void Add(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        _rows.Add(new KeyValuePair<string, string>(name, value));
    }
}
