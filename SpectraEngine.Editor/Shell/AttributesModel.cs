using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The Attributes section of the Properties panel: the attributes of the
/// selection as rows of name, kind and value, and a line that adds one to
/// every selected node. UI thread only.
/// </summary>
// Shows what the engine last said and nothing it hopes for. Rows are kept
// while the names and kinds stay the same, so a cell somebody types in is
// not thrown away 30 times a second.
public sealed class AttributesModel : ObservableObject
{
    private readonly Action<AttributeEdit> _apply;
    private AttributePanelInfo? _info;
    private string _newKindLabel = AttributeKindWords.LabelOf(MemberValueKind.Text);

    internal AttributesModel(Action<AttributeEdit> apply)
    {
        _apply = apply;
        NewNameField = new PropertyFieldModel(PropertyId.None, PropertyAxes.All, string.Empty, Add);
    }

    /// <summary>The attributes, in the order the selection has them.</summary>
    public ObservableCollection<AttributeRowModel> Rows { get; } = [];

    /// <summary>The field a new attribute's name is typed into. Enter adds it.</summary>
    public PropertyFieldModel NewNameField { get; }

    /// <summary>The words for the kinds, for the dropdown beside the new name.</summary>
    public IReadOnlyList<string> KindLabels => AttributeKindWords.Labels;

    /// <summary>What the kind dropdowns say when the pointer rests on them.</summary>
    public string KindHelp => AttributeKindWords.Help;

    /// <summary>The kind a new attribute gets. It stays as picked for the next one.</summary>
    public string NewKindLabel
    {
        get => _newKindLabel;
        set
        {
            if (AttributeKindWords.KindOf(value) != MemberValueKind.None)
                Set(ref _newKindLabel, value);
        }
    }

    /// <summary>Takes one published snapshot's attributes. Null when nothing is selected.</summary>
    public void Apply(AttributePanelInfo? info)
    {
        // The engine hands back the same instance while nothing changed.
        if (ReferenceEquals(info, _info))
            return;

        _info = info;
        IReadOnlyList<AttributePanelRow> rows = info?.Rows ?? [];

        if (!HasRowsFor(rows))
        {
            Rows.Clear();
            foreach (AttributePanelRow row in rows)
                Rows.Add(new AttributeRowModel(row.Name, row.Kind, this));
        }

        for (int i = 0; i < rows.Count; i++)
            Rows[i].Refresh(rows[i], info?.SelectionCount ?? 0);
    }

    /// <summary>Takes the row's attribute off every selected node.</summary>
    public void Remove(AttributeRowModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _apply(AttributeEdit.Remove(row.Name));
    }

    /// <summary>Adds what the new-name field holds, as Enter in it does.</summary>
    public void AddTyped()
    {
        NewNameField.BeginEdit();
        NewNameField.Commit();
    }

    /// <summary>
    /// Why no attribute of the selection can get this name, as a sentence, or
    /// null when one can.
    /// </summary>
    /// <param name="isNew">True for a new attribute, which also needs room on every node.</param>
    public string? NameRefusal(string name, bool isNew)
    {
        if (PropertyLimits.AttributeNameRefusal(name) is { } broken)
            return broken;

        if (_info is not { } info)
            return "Nothing is selected.";

        if (isNew && info.HoldsRoot)
            return PropertyLimits.RootKeepsNone;

        foreach (AttributePanelRow row in info.Rows)
        {
            if (string.Equals(row.Name, name, StringComparison.Ordinal))
                return $"There is an attribute called '{name}' already.";
        }

        if (info.EntityNames.Contains(name))
            return PropertyLimits.TakenByEntity(name);

        return isNew && info.HasFullNode ? PropertyLimits.FullOfAttributes : null;
    }

    internal void Post(AttributeEdit edit) => _apply(edit);

    // Shows the last published attributes again. A colour dragged in the
    // picker is shown before the engine says it. A cancel can put the level
    // back to the very sets this saw last, and Apply takes those for no news.
    internal void ShowAgain()
    {
        AttributePanelInfo? info = _info;
        _info = null;
        Apply(info);
    }

    // What was typed stays in the box with the reason under it, so it can
    // be put right. Reject alone would empty the box.
    private void Add(PropertyFieldModel field, string typed)
    {
        if (NameRefusal(typed, isNew: true) is { } whyNot)
        {
            field.Reject($"Not added: {whyNot}");
            field.Text = typed;
            return;
        }

        _apply(AttributeEdit.Set(typed, AttributeParts.DefaultOf(AttributeKindWords.KindOf(_newKindLabel))));
        field.Revert();
    }

    private bool HasRowsFor(IReadOnlyList<AttributePanelRow> rows)
    {
        if (Rows.Count != rows.Count)
            return false;

        for (int i = 0; i < rows.Count; i++)
        {
            if (Rows[i].Kind != rows[i].Kind || !string.Equals(Rows[i].Name, rows[i].Name, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
