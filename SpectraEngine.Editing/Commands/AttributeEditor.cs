using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Applies an attribute edit to a selection as one history entry. A node
/// that is already as the edit wants it records nothing. Render thread only.
/// </summary>
// Every refusal here has its words in PropertyLimits, where the panel reads
// them before it posts. A node this skips is one the panel said it would.
public static class AttributeEditor
{
    /// <summary>
    /// Writes <paramref name="edit"/> to every node in
    /// <paramref name="targets"/> it applies to. A name or a value the rules
    /// refuse changes nothing. A node is left as it is when it has no room
    /// for one more attribute, or carries an entity with a keyvalue of the
    /// name.
    /// </summary>
    /// <param name="inGesture">
    /// True while the caller holds a transaction open, so this edit joins it
    /// instead of becoming its own history entry.
    /// </param>
    /// <returns>How many nodes changed.</returns>
    public static int Apply(
        UndoStack undo, IReadOnlyList<SceneNode> targets, AttributeEdit edit, bool inGesture = false)
    {
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(targets);

        if (edit.Name is null || IsRefused(in edit))
            return 0;

        EntitySchemaCatalog? schemas = undo.Scene.EntitySchemas;
        var commands = new List<IEditorCommand>();
        foreach (SceneNode node in targets)
        {
            if (node is null) continue;

            AttributeSet next = Next(node, in edit, schemas);
            if (!next.Equals(node.Attributes))
                commands.Add(new SetAttributesCommand(node.Id, node.Attributes, next) { Name = NameOf(edit.Kind) });
        }

        if (commands.Count == 0)
            return 0;

        // Inside a gesture the caller owns the transaction; a second Begin throws.
        if (!inGesture)
            undo.BeginTransaction(NameOf(edit.Kind));

        foreach (IEditorCommand command in commands)
            undo.Execute(command);

        if (!inGesture)
            undo.CommitTransaction();

        return commands.Count;
    }

    // What no node can take, whichever node it is.
    private static bool IsRefused(in AttributeEdit edit)
    {
        MemberValue value = edit.Value;
        return edit.Kind switch
        {
            AttributeEditKind.Set =>
                PropertyLimits.AttributeNameRefusal(edit.Name) is not null
                || PropertyLimits.AttributeValueRefusal(in value) is not null,

            AttributeEditKind.SetPart =>
                PropertyLimits.AttributeNameRefusal(edit.Name) is not null
                || edit.Part < 0 || edit.Part >= AttributeParts.CountOf(value.Kind)
                || !float.IsFinite(edit.Number),

            AttributeEditKind.Rename =>
                edit.NewName is null || PropertyLimits.AttributeNameRefusal(edit.NewName) is not null,

            _ => false,
        };
    }

    // The node's attributes after the edit, or the ones it has when the
    // edit does not apply to it.
    private static AttributeSet Next(SceneNode node, in AttributeEdit edit, EntitySchemaCatalog? schemas)
    {
        AttributeSet set = node.Attributes;
        bool has = set.TryGet(edit.Name, out MemberValue held);

        switch (edit.Kind)
        {
            case AttributeEditKind.Remove:
                return set.Without(edit.Name);

            case AttributeEditKind.Rename:
                return has && !set.Contains(edit.NewName) && !EntityKeyvalueNames.Contains(node, schemas, edit.NewName)
                    ? set.Renamed(edit.Name, edit.NewName)
                    : set;

            case AttributeEditKind.Set:
                return has || HasRoomFor(node, edit.Name, schemas) ? set.With(edit.Name, edit.Value) : set;

            default:
                if (!has && !HasRoomFor(node, edit.Name, schemas))
                    return set;

                MemberValue from = has && held.Kind == edit.Value.Kind ? held : edit.Value;
                MemberValue next = AttributeParts.With(in from, edit.Part, edit.Number);
                return PropertyLimits.AttributeValueRefusal(in next) is null ? set.With(edit.Name, next) : set;
        }
    }

    // Whether the node can get an attribute of a name it does not have.
    private static bool HasRoomFor(SceneNode node, string name, EntitySchemaCatalog? schemas) =>
        !node.Attributes.IsFull && !EntityKeyvalueNames.Contains(node, schemas, name);

    private static string NameOf(AttributeEditKind kind) => kind switch
    {
        AttributeEditKind.Remove => "Remove Attribute",
        AttributeEditKind.Rename => "Rename Attribute",
        _ => "Attribute",
    };
}
