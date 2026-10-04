using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// What a placed entity carries: the class it names, the keyvalues authored on
/// it, and the wires leaving its outputs. Mutable, so a duplicated node needs
/// its own <see cref="Clone"/>.
/// </summary>
// The class is a name, not a schema reference: a map authored against a game
// this build doesn't have must still load and save.
public sealed class EntityData
{
    /// <summary>An entity carrying no class yet.</summary>
    public EntityData()
    {
    }

    /// <summary>An entity of the named class.</summary>
    public EntityData(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        ClassName = className;
    }

    /// <summary>
    /// The entity class this instance is, as a map file spells it. Empty until
    /// one is assigned.
    /// </summary>
    public string ClassName { get; set; } = "";

    /// <summary>
    /// The authored keyvalues, name to value, in authored order. Names match
    /// ordinally.
    /// </summary>
    // A list, not a dictionary: member order must round-trip through the map
    // file byte for byte.
    public List<KeyValuePair<string, string>> Keyvalues { get; } = [];

    /// <summary>The wires leaving this entity's outputs, in authored order.</summary>
    public List<EntityConnection> Connections { get; } = [];

    /// <summary>
    /// Reads the value authored for <paramref name="name"/>. The first match wins.
    /// </summary>
    public bool TryGetValue(string name, out string value)
    {
        for (int i = 0; i < Keyvalues.Count; i++)
        {
            if (string.Equals(Keyvalues[i].Key, name, StringComparison.Ordinal))
            {
                value = Keyvalues[i].Value;
                return true;
            }
        }

        value = "";
        return false;
    }

    /// <summary>
    /// Writes <paramref name="name"/>, replacing an existing entry in place so
    /// it keeps its position in the file, and appending otherwise.
    /// </summary>
    public void SetValue(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        for (int i = 0; i < Keyvalues.Count; i++)
        {
            if (string.Equals(Keyvalues[i].Key, name, StringComparison.Ordinal))
            {
                Keyvalues[i] = new KeyValuePair<string, string>(name, value);
                return;
            }
        }

        Keyvalues.Add(new KeyValuePair<string, string>(name, value));
    }

    /// <summary>An independent copy carrying the same class, keyvalues and wires.</summary>
    public EntityData Clone()
    {
        var copy = new EntityData(ClassName);
        copy.Keyvalues.AddRange(Keyvalues);
        copy.Connections.AddRange(Connections);
        return copy;
    }
}
