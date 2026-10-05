using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Entities;

/// <summary>
/// The anchor a host calls to make sure this assembly's entity classes are in
/// the catalogue.
/// </summary>
// Classes register in generated module initializers, which only run once this
// assembly is loaded. Nothing calls into it statically (maps name classes as
// text), so a trimmed or AOT build would drop it and every entity would load
// as a placeholder. Call before the first EntityWorld.Activate.
public static class BuiltinEntities
{
    /// <summary>How many entity classes this assembly declares.</summary>
    // A constant, so a class dropped from Schemas throws instead of going unanchored.
    public const int ClassCount = 14;

    /// <summary>Every built-in class's schema, in declaration order.</summary>
    // Touching each class here is what keeps the trimmer from removing it.
    public static IReadOnlyList<EntitySchema> Schemas { get; } =
    [
        FuncDoor.SpectraSchema,
        FuncMoveLinear.SpectraSchema,
        InfoPlayerStart.SpectraSchema,
        InfoTeleportDestination.SpectraSchema,
        LogicAuto.SpectraSchema,
        LogicBranch.SpectraSchema,
        LogicCase.SpectraSchema,
        LogicCompare.SpectraSchema,
        LogicRelay.SpectraSchema,
        LogicTimer.SpectraSchema,
        MathCounter.SpectraSchema,
        TriggerMultiple.SpectraSchema,
        TriggerOnce.SpectraSchema,
        TriggerTeleport.SpectraSchema,
    ];

    /// <summary>
    /// Loads this assembly, which runs the generated registrations. Throws if a
    /// class is missing from <see cref="Schemas"/>.
    /// </summary>
    public static void EnsureRegistered()
    {
        // The comparison also stops the JIT eliding the read of Schemas.
        if (Schemas.Count != ClassCount)
        {
            throw new InvalidOperationException(
                $"The built-in entity anchor lists {Schemas.Count} classes and expects {ClassCount}. " +
                "A class missing from the anchor is a class a trimmed build may drop, which makes every " +
                "map naming it load as a placeholder that behaves as nothing.");
        }
    }
}
