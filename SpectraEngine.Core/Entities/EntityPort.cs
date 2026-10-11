using System;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// One input or output of an entity class: the name a wire spells it by, and
/// what it takes or sends.
/// </summary>
/// <param name="Name">The wire name, as a map file spells it.</param>
/// <param name="Parameter">What an input takes, or an output sends.</param>
public readonly record struct EntityPort(string Name, ParameterType Parameter)
{
    /// <summary>
    /// Ports that have a name and do not say what they carry, in the order
    /// given.
    /// </summary>
    // Not an implicit conversion from string: inputs.Contains("Open") would
    // then compile and never find a port that says its type.
    public static EntityPort[] Named(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var ports = new EntityPort[names.Length];
        for (int i = 0; i < ports.Length; i++)
            ports[i] = new EntityPort(names[i], ParameterType.NotSaid);

        return ports;
    }
}
