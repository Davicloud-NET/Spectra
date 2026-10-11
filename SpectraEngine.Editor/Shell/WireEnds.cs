using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

// The classes at the two ends of one wire, as far as the session knows them,
// and what they say about the wire's parameter.
internal sealed class WireEnds
{
    private IReadOnlyList<EntityTargetInfo> _targets = [];
    private EntitySchemaCatalog? _schemas;

    // The class of the entity the wire leaves, or null.
    public EntitySchema? Sender { get; set; }

    // The class of the one entity the wire's target names, or null.
    public EntitySchema? Receiver { get; private set; }

    // Takes the entities a target is looked up among.
    public void Look(IReadOnlyList<EntityTargetInfo> targets, EntitySchemaCatalog? schemas)
    {
        _targets = targets;
        _schemas = schemas;
    }

    public void Aim(string target) => Receiver = ReceiverFor(target, _targets, _schemas);

    // The line under the wire about its parameter, or empty. A wire to !self
    // arrives at the class it leaves.
    public string ParameterWarning(in EntityConnection wire) => WireParameterText.Between(
        in wire, Sender, wire.TargetName == TargetNameIndex.SelfToken ? Sender : Receiver);

    private static EntitySchema? ReceiverFor(
        string target,
        IReadOnlyList<EntityTargetInfo> targets,
        EntitySchemaCatalog? schemas)
    {
        if (schemas is null || string.IsNullOrEmpty(target)) return null;

        // A runtime token or a wildcard has no single class.
        if (target[0] == '!' || target[^1] == '*') return null;

        string? className = null;
        for (int i = 0; i < targets.Count; i++)
        {
            if (!string.Equals(targets[i].Name, target, StringComparison.Ordinal)) continue;

            // Two entities share the name (legal: the wire fires at both).
            if (className is not null) return null;

            className = targets[i].ClassName;
        }

        return schemas.TryGetSchema(className, out EntitySchema? schema) ? schema : null;
    }
}
