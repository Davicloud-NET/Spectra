using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// The edits the Logic view makes to an entity's wires. An edit replaces the
/// whole list, so each one gives the list as it should be afterwards.
/// </summary>
public static class LogicWireList
{
    /// <summary>The wire a drop makes: it fires forever, at once, and sends no parameter.</summary>
    public static EntityConnection NewWire(string output, string target, string input) =>
        new(output, target, input, "", 0f, EntityConnection.Infinite);

    /// <summary>
    /// The target a wire from one card needs to reach another, or null when
    /// nothing reaches it. A wire finds an entity by its name. An entity
    /// without one can still be wired to itself.
    /// </summary>
    public static string? TargetOf(LogicCard from, LogicCard to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (to.IsStub)
            return null;

        // A name that starts with ! is read as a token and matches nothing.
        if (to.Name.Length > 0 && to.Name[0] != '!')
            return to.Name;

        return ReferenceEquals(from, to) ? TargetNameIndex.SelfToken : null;
    }

    /// <summary>The sender's wires with one more at the end.</summary>
    /// <param name="info">The level's wiring as it was last published.</param>
    /// <param name="sender">The entity that sends.</param>
    /// <param name="wire">The wire to add.</param>
    /// <param name="wires">The new list, or an empty one when the edit is refused.</param>
    public static LogicWireRefusal Add(
        LogicGraphInfo? info,
        Guid sender,
        EntityConnection wire,
        out EntityConnection[] wires)
    {
        wires = [];
        if (!TryFind(info, sender, out IReadOnlyList<EntityConnection> before))
            return LogicWireRefusal.SenderGone;

        for (int i = 0; i < before.Count; i++)
        {
            if (before[i] == wire)
                return LogicWireRefusal.AlreadyThere;
        }

        wires = new EntityConnection[before.Count + 1];
        for (int i = 0; i < before.Count; i++)
            wires[i] = before[i];

        wires[^1] = wire;
        return LogicWireRefusal.None;
    }

    /// <summary>The sender's wires without the one at an index.</summary>
    /// <param name="info">The level's wiring as it was last published.</param>
    /// <param name="sender">The entity that sends.</param>
    /// <param name="index">Where the wire is in the sender's list.</param>
    /// <param name="wires">The new list, or an empty one when the edit is refused.</param>
    public static LogicWireRefusal Remove(
        LogicGraphInfo? info,
        Guid sender,
        int index,
        out EntityConnection[] wires)
    {
        wires = [];
        if (!TryFind(info, sender, out IReadOnlyList<EntityConnection> before))
            return LogicWireRefusal.SenderGone;

        if (index < 0 || index >= before.Count)
            return LogicWireRefusal.WireGone;

        wires = new EntityConnection[before.Count - 1];
        for (int i = 0; i < wires.Length; i++)
            wires[i] = before[i < index ? i : i + 1];

        return LogicWireRefusal.None;
    }

    private static bool TryFind(LogicGraphInfo? info, Guid sender, out IReadOnlyList<EntityConnection> wires)
    {
        IReadOnlyList<LogicEntityInfo> entities = info?.Entities ?? [];
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i].NodeId == sender)
            {
                wires = entities[i].Wires;
                return true;
            }
        }

        wires = [];
        return false;
    }
}
