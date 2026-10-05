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
    private readonly List<KeyValuePair<string, string>>? _rows;
    private readonly EntityHeadline? _headline;

    /// <param name="rows">Gets one name and value per <c>Add</c>, in the order they are made.</param>
    public EntityStateWriter(List<KeyValuePair<string, string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows;
    }

    /// <summary>A writer that keeps the headline and drops every row.</summary>
    public EntityStateWriter(EntityHeadline headline)
    {
        ArgumentNullException.ThrowIfNull(headline);
        _headline = headline;
    }

    /// <summary>
    /// Whether rows are being dropped. Check it before building a value that
    /// costs something to format.
    /// </summary>
    public bool WantsHeadlineOnly => _rows is null;

    /// <summary>
    /// Says in one short line where the entity stands, for a place with room
    /// for one. Call it once, before the rows.
    /// </summary>
    /// <param name="label">What the value is, such as "opening" or "value".</param>
    /// <param name="value">The value, such as "14 of 39 ticks".</param>
    public void Headline(string label, string value)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);

        _headline?.Set(label, value);
    }

    // Each typed Add returns before it formats when rows are dropped: a
    // headline is read several times a second for many entities.

    /// <summary>Adds a flag, as <c>1</c> or <c>0</c>.</summary>
    public void Add(string name, bool value)
    {
        if (_rows is not null)
            Add(name, KeyvalueWire.Format(value));
    }

    /// <summary>Adds a whole number.</summary>
    public void Add(string name, int value)
    {
        if (_rows is not null)
            Add(name, KeyvalueWire.Format(value));
    }

    /// <summary>Adds a number. Infinity and NaN are written out, not refused.</summary>
    // Not KeyvalueWire.Format: it throws on a float a map could not hold, and
    // a broken value is what someone reading state is looking for.
    public void Add(string name, float value)
    {
        if (_rows is not null)
            Add(name, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Adds text as it is.</summary>
    public void Add(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        _rows?.Add(new KeyValuePair<string, string>(name, value));
    }
}
