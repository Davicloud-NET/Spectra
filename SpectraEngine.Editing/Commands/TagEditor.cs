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
    /// <param name="inGesture">
    /// True while the caller holds a transaction open, so this edit joins it
    /// instead of becoming its own history entry.
    /// </param>
    /// <returns>How many nodes changed.</returns>
    public static int Apply(UndoStack undo, IReadOnlyList<SceneNode> targets, TagEdit edit, bool inGesture = false)
    {
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(targets);

        if (edit.Tag is null || (edit.IsOn && PropertyLimits.TagRefusal(edit.Tag) is not null))
            return 0;

        var commands = new List<IEditorCommand>();
        foreach (SceneNode node in targets)
        {
            if (node is null) continue;

            TagSet next = Next(node.Tags, edit);
            if (!ReferenceEquals(next, node.Tags))
                commands.Add(new SetTagsCommand(node.Id, node.Tags, next) { Name = NameOf(edit) });
        }

        if (commands.Count == 0)
            return 0;

        // Inside a gesture the caller owns the transaction; a second Begin throws.
        if (!inGesture)
            undo.BeginTransaction(NameOf(edit));

        foreach (IEditorCommand command in commands)
            undo.Execute(command);

        if (!inGesture)
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
