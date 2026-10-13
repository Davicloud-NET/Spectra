using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Applies a tag edit to a selection as one history entry. A node that is
/// already as the edit wants it records nothing. Render thread only.
/// </summary>
public static class TagEditor
{
    /// <summary>
    /// Puts the tag on every node in <paramref name="targets"/>, or takes it
    /// off every one. A tag the rule refuses changes nothing, and a node
    /// with no room for one more is left as it is.
    /// </summary>
    /// <returns>How many nodes changed.</returns>
    public static int Apply(UndoStack undo, IReadOnlyList<SceneNode> targets, TagEdit edit)
    {
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(targets);

        if (edit.Tag is null || (edit.IsOn && PropertyLimits.TagRefusal(edit.Tag) is not null))
            return 0;

        var commands = new List<IEditorCommand>();
        foreach (SceneNode node in targets)
        {
            // The root is not saved with the level, so it takes no tag. One
            // it has already can still come off.
            if (node is null || (edit.IsOn && node.Parent is null)) continue;

            TagSet next = Next(node.Tags, edit);
            if (!ReferenceEquals(next, node.Tags))
                commands.Add(new SetTagsCommand(node.Id, node.Tags, next) { Name = NameOf(edit) });
        }

        if (commands.Count == 0)
            return 0;

        // A tag edit is always its own entry. No gesture carries one, and the
        // editor keeps it out of one that is open.
        undo.BeginTransaction(NameOf(edit));
        foreach (IEditorCommand command in commands)
            undo.Execute(command);

        undo.CommitTransaction();
        return commands.Count;
    }

    private static TagSet Next(TagSet tags, TagEdit edit)
    {
        if (!edit.IsOn)
            return tags.Without(edit.Tag);

        return tags.IsFull ? tags : tags.With(edit.Tag);
    }

    private static string NameOf(TagEdit edit) => edit.IsOn ? "Add Tag" : "Remove Tag";
}
