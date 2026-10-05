using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// One box in the wiring graph: an entity, or a stub standing in for a target
/// that is not an entity.
/// </summary>
public sealed class LogicCard
{
    internal LogicCard(
        int index,
        LogicEntityInfo entity,
        EntitySchema? schema,
        IReadOnlyList<LogicPort> inputs,
        IReadOnlyList<LogicPort> outputs)
    {
        Index = index;
        NodeId = entity.NodeId;
        Name = entity.Name;
        ClassName = entity.ClassName;
        DisplayName = string.IsNullOrEmpty(schema?.DisplayName) ? entity.ClassName : schema.DisplayName;
        Group = schema?.Group ?? "";
        IsKnownClass = schema is not null;
        Inputs = inputs;
        Outputs = outputs;
        IsWired = HasWire(inputs) || HasWire(outputs);
    }

    internal LogicCard(int index, LogicStubKind stub, string name, IReadOnlyList<LogicPort> inputs)
    {
        Index = index;
        Stub = stub;
        Name = name;
        ClassName = "";
        DisplayName = LogicText.StubLine(stub);
        Group = "";
        Inputs = inputs;
        Outputs = [];
        IsWired = true;
    }

    /// <summary>Where this card is in <see cref="LogicGraph.Cards"/>.</summary>
    public int Index { get; }

    /// <summary>The entity's node. Empty for a stub.</summary>
    public Guid NodeId { get; }

    /// <summary>The entity's name, or for a stub the target a wire spelled.</summary>
    public string Name { get; }

    /// <summary>The class as the map spells it. Empty for a stub.</summary>
    public string ClassName { get; }

    /// <summary>
    /// The line under the name: the class's display name, the class name when
    /// it has none, or for a stub what is wrong with the target.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>The category the class is filed under, or empty.</summary>
    public string Group { get; }

    /// <summary>Whether a schema describes the class. False for a stub.</summary>
    public bool IsKnownClass { get; }

    /// <summary>What this card stands for when it is not an entity.</summary>
    public LogicStubKind Stub { get; }

    /// <summary>Whether this card stands in for a target that is not an entity.</summary>
    public bool IsStub => Stub != LogicStubKind.None;

    /// <summary>
    /// Whether a wire leaves the card or arrives at it. An entity with none
    /// is shown only while it is selected.
    /// </summary>
    public bool IsWired { get; }

    /// <summary>
    /// Every input the class declares, in its order, then the ones only a
    /// wire names, by name.
    /// </summary>
    public IReadOnlyList<LogicPort> Inputs { get; }

    /// <summary>
    /// Every output the class declares, in its order, then the ones only a
    /// wire names, by name.
    /// </summary>
    public IReadOnlyList<LogicPort> Outputs { get; }

    /// <summary>Whether the class declares an input or output of this name.</summary>
    public bool Declares(string name, bool isOutput)
    {
        foreach (LogicPort port in isOutput ? Outputs : Inputs)
        {
            if (port.IsDeclared && string.Equals(port.Name, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasWire(IReadOnlyList<LogicPort> ports)
    {
        foreach (LogicPort port in ports)
        {
            if (port.IsWired)
                return true;
        }

        return false;
    }
}
