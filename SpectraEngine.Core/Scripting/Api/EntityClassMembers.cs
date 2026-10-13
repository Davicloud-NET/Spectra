using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// The members a script finds on a node of one entity class: each input as a
/// method and each output as a signal. <see cref="EntityClassApi.MembersOf"/>
/// reads them from the class's schema, and both the binding and the
/// description of the class go by this.
/// </summary>
/// <param name="ClassName">The entity class, as a map spells it.</param>
/// <param name="Extends">The script class of the node that carries the entity, whose own members win.</param>
/// <param name="Inputs">The inputs a script calls, in the schema's order.</param>
/// <param name="Outputs">The outputs a script connects to, in the schema's order.</param>
public sealed record EntityClassMembers(
    string ClassName,
    string Extends,
    IReadOnlyList<EntityPort> Inputs,
    IReadOnlyList<EntityPort> Outputs)
{
    /// <summary>The input of that name. Names are case sensitive, as a wire's are.</summary>
    public bool TryGetInput(ReadOnlySpan<char> name, out EntityPort input) => TryFind(Inputs, name, out input);

    /// <summary>The output of that name. Names are case sensitive, as a wire's are.</summary>
    public bool TryGetOutput(ReadOnlySpan<char> name, out EntityPort output) => TryFind(Outputs, name, out output);

    private static bool TryFind(IReadOnlyList<EntityPort> ports, ReadOnlySpan<char> name, out EntityPort found)
    {
        for (int i = 0; i < ports.Count; i++)
        {
            if (name.SequenceEqual(ports[i].Name))
            {
                found = ports[i];
                return true;
            }
        }

        found = default;
        return false;
    }
}
