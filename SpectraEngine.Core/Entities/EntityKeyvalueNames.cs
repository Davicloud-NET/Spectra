using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The names the entity a node carries answers to: the keyvalues its class
/// declares and those only the node holds. A script reads each as an
/// attribute, so no attribute a builder adds may have one of them.
/// </summary>
public static class EntityKeyvalueNames
{
    /// <summary>Whether the node's entity has a keyvalue of that name. Names are case sensitive.</summary>
    /// <param name="schemas">What the classes declare, or null. Without it only the node's own count.</param>
    public static bool Contains(SceneNode node, EntitySchemaCatalog? schemas, string name)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Entity is not { ClassName.Length: > 0 } entity)
            return false;

        if (schemas is not null && schemas.TryGetSchema(entity.ClassName, out EntitySchema? schema))
        {
            IReadOnlyList<KeyvalueDescriptor> declared = schema.Keyvalues;
            for (int i = 0; i < declared.Count; i++)
            {
                if (string.Equals(declared[i].Name, name, StringComparison.Ordinal))
                    return true;
            }
        }

        List<KeyValuePair<string, string>> keyvalues = entity.Keyvalues;
        for (int i = 0; i < keyvalues.Count; i++)
        {
            if (string.Equals(keyvalues[i].Key, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Adds every such name of the node to a set.</summary>
    public static void AddTo(SceneNode node, EntitySchemaCatalog? schemas, ISet<string> names)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(names);

        if (node.Entity is not { ClassName.Length: > 0 } entity)
            return;

        if (schemas is not null && schemas.TryGetSchema(entity.ClassName, out EntitySchema? schema))
        {
            foreach (KeyvalueDescriptor declared in schema.Keyvalues)
                names.Add(declared.Name);
        }

        foreach (KeyValuePair<string, string> pair in entity.Keyvalues)
            names.Add(pair.Key);
    }
}
