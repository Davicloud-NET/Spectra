using SpectraEngine.Core.Inspection;
using SpectraEngine.Editing.Commands;
using System;
using System.Collections.ObjectModel;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The Tags section of the Properties panel: the tags of the selection as
/// chips, and a field that adds one to every selected node. UI thread only.
/// </summary>
// Shows what the engine last said and nothing it hopes for: a chip appears
// when the snapshot after the edit says the tag is there.
public sealed class TagsModel : ObservableObject
{
    private readonly Action<TagEdit> _apply;
    private TagPanelInfo? _info;

    internal TagsModel(Action<TagEdit> apply)
    {
        _apply = apply;
        AddField = new PropertyFieldModel(PropertyId.None, PropertyAxes.All, string.Empty, Add);
    }

    /// <summary>The tags, in the order the selection has them.</summary>
    public ObservableCollection<TagChipModel> Chips { get; } = [];

    /// <summary>The field a new tag is typed into. Enter adds it.</summary>
    public PropertyFieldModel AddField { get; }

    /// <summary>Whether the selection has no tag at all.</summary>
    public bool IsEmpty => Chips.Count == 0;

    /// <summary>Takes one published snapshot's tags. Null when nothing is selected.</summary>
    public void Apply(TagPanelInfo? info)
    {
        // The engine hands back the same instance while nothing changed.
        if (ReferenceEquals(info, _info))
            return;

        _info = info;
        bool wasEmpty = IsEmpty;

        Chips.Clear();
        if (info is not null)
        {
            foreach (TagPanelRow row in info.Tags)
                Chips.Add(new TagChipModel(row.Tag, row.Count, info.SelectionCount));
        }

        if (wasEmpty != IsEmpty)
            Raise(nameof(IsEmpty));
    }

    /// <summary>Takes the chip's tag off every selected node.</summary>
    public void Remove(TagChipModel chip)
    {
        ArgumentNullException.ThrowIfNull(chip);
        _apply(new TagEdit(chip.Tag, IsOn: false));
    }

    /// <summary>
    /// Why the selection cannot take this tag, as a sentence, or null when
    /// it can.
    /// </summary>
    public string? Refusal(string tag)
    {
        if (PropertyLimits.TagRefusal(tag) is { } broken)
            return broken;

        if (_info is not { } info)
            return "Nothing is selected.";

        foreach (TagPanelRow row in info.Tags)
        {
            if (string.Equals(row.Tag, tag, StringComparison.Ordinal) && row.Count == info.SelectionCount)
            {
                return info.SelectionCount > 1
                    ? $"Every selected node has the tag '{tag}' already."
                    : $"It has the tag '{tag}' already.";
            }
        }

        return info.HasFullNode ? PropertyLimits.FullOfTags : null;
    }

    // What was typed stays in the box with the reason under it, so it can
    // be put right. Reject alone would empty the box.
    private void Add(PropertyFieldModel field, string typed)
    {
        if (Refusal(typed) is { } whyNot)
        {
            field.Reject($"Not added: {whyNot}");
            field.Text = typed;
            return;
        }

        _apply(new TagEdit(typed, IsOn: true));
        field.Revert();
    }
}
