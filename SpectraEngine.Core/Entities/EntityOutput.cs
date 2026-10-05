using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// One of an entity's outputs, holding the runtime copy of the wires the map
/// authored on it. Wires fire in authored order.
/// </summary>
// Remaining fire counts live here, never on the EntityConnection in EntityData,
// or playing a level would edit the map that gets saved.
public sealed class EntityOutput
{
    private readonly List<Wire> _wires = [];

    internal EntityOutput(string name) => Name = name;

    /// <summary>The output's name, as the map spells it.</summary>
    public string Name { get; }

    /// <summary>How many wires leave this output, exhausted ones included.</summary>
    public int WireCount => _wires.Count;

    /// <summary>How many wires can still fire.</summary>
    public int LiveWireCount
    {
        get
        {
            int live = 0;
            for (int i = 0; i < _wires.Count; i++)
            {
                if (_wires[i].FiresLeft != 0)
                    live++;
            }

            return live;
        }
    }

    /// <summary>
    /// The fires remaining on the wire at <paramref name="index"/>, negative for
    /// unlimited.
    /// </summary>
    public int FiresLeftAt(int index) => _wires[index].FiresLeft;

    /// <summary>The connection the wire at <paramref name="index"/> was built from.</summary>
    public EntityConnection ConnectionAt(int index) => _wires[index].Connection;

    // authoredIndex is the wire's place in the entity's whole connection list,
    // which is how a trace names it. Its place in this output differs.
    internal void Add(in EntityConnection connection, int authoredIndex) =>
        _wires.Add(new Wire
        {
            Connection = connection,
            FiresLeft = connection.TimesToFire,
            AuthoredIndex = authoredIndex,
        });

    internal void Fire(Entity caller, Entity? activator, string? parameterOverride)
    {
        EntityWorld world = caller.World;
        for (int i = 0; i < _wires.Count; i++)
        {
            Wire wire = _wires[i];
            // Zero is exhausted. Negative is infinite and never decremented.
            if (wire.FiresLeft == 0)
                continue;

            world.ScheduleOutput(caller, activator, Name, wire.Connection, wire.AuthoredIndex, parameterOverride);

            if (wire.FiresLeft > 0)
            {
                wire.FiresLeft--;
                // Wire is a struct; the local is a copy.
                _wires[i] = wire;
            }
        }
    }

    private struct Wire
    {
        public EntityConnection Connection;
        public int FiresLeft;
        public int AuthoredIndex;
    }
}
