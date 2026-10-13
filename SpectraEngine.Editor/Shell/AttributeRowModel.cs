using Avalonia.Media;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Commands;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// One attribute of the selection in the Properties panel: its name, the
/// kind of its value, and the editor that kind gets.
/// </summary>
// A row is one name with one kind. When either changes the panel makes a
// new row, so a row never has to turn one editor into another.
public sealed class AttributeRowModel : ObservableObject
{
    private static readonly string[] AxisLabels = ["x", "y", "z"];
    private static readonly PropertyAxes[] Axes = [PropertyAxes.X, PropertyAxes.Y, PropertyAxes.Z];

    private readonly AttributesModel _owner;
    private bool _applyingRefresh;
    private bool? _flag;
    private bool _isPartial;
    private bool _isTakenByEntity;
    private string _partialNote = string.Empty;
    private Color3? _color;
    private IBrush _swatch = Brushes.Transparent;

    internal AttributeRowModel(string name, MemberValueKind kind, AttributesModel owner)
    {
        Name = name;
        Kind = kind;
        _owner = owner;

        NameField = new PropertyFieldModel(PropertyId.None, PropertyAxes.All, string.Empty, CommitName);
        Cells = kind switch
        {
            MemberValueKind.Vector3 or MemberValueKind.CFrame => PartCells(first: 0),
            MemberValueKind.Text => [Cell(CommitWhole, allowsEmpty: true)],
            MemberValueKind.Number or MemberValueKind.Color => [Cell(CommitWhole, allowsEmpty: false)],
            _ => [],
        };
        TurnCells = kind == MemberValueKind.CFrame ? PartCells(first: 3) : [];

        foreach (PropertyFieldModel field in AllFields())
        {
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(PropertyFieldModel.Rejection))
                {
                    Raise(nameof(Rejection));
                    Raise(nameof(HasRejection));
                }
            };
        }
    }

    /// <summary>The attribute's name.</summary>
    public string Name { get; }

    /// <summary>The kind of its value, or none when the selected nodes hold different kinds.</summary>
    public MemberValueKind Kind { get; }

    /// <summary>The name as a field. Committing another name renames the attribute.</summary>
    public PropertyFieldModel NameField { get; }

    /// <summary>The value's cells: one for text, a number or a colour, three for three numbers or a position.</summary>
    public IReadOnlyList<PropertyFieldModel> Cells { get; }

    /// <summary>The three cells of a rotation, in degrees. Empty for every other kind.</summary>
    public IReadOnlyList<PropertyFieldModel> TurnCells { get; }

    /// <summary>The words for the kinds, for the dropdown.</summary>
    public IReadOnlyList<string> KindLabels => AttributeKindWords.Labels;

    /// <summary>
    /// The dropdown's value. Picking another kind gives the attribute that
    /// kind's starting value on every selected node.
    /// </summary>
    public string KindLabel
    {
        get => AttributeKindWords.LabelOf(Kind);
        set
        {
            MemberValueKind picked = AttributeKindWords.KindOf(value);
            if (_applyingRefresh || picked == MemberValueKind.None || picked == Kind)
                return;

            _owner.Post(AttributeEdit.Set(Name, AttributeParts.DefaultOf(picked)));
        }
    }

    public bool IsText => Kind == MemberValueKind.Text;
    public bool IsNumber => Kind == MemberValueKind.Number;
    public bool IsBoolean => Kind == MemberValueKind.Boolean;
    public bool IsVector => Kind == MemberValueKind.Vector3;
    public bool IsColor => Kind == MemberValueKind.Color;
    public bool IsFrame => Kind == MemberValueKind.CFrame;

    /// <summary>Whether the value is one cell beside nothing else: text or a number.</summary>
    public bool IsOneCell => IsText || IsNumber;

    /// <summary>Whether the cells are three numbers in a row: three numbers, or a frame's position.</summary>
    public bool HasParts => IsVector || IsFrame;

    /// <summary>Whether the selected nodes hold values of different kinds, so there is none to edit.</summary>
    public bool IsMixedKind => Kind == MemberValueKind.None;

    /// <summary>The checkbox of a true or false value. Null while the selection disagrees.</summary>
    public bool? Flag
    {
        get => _flag;
        set
        {
            if (!Set(ref _flag, value) || _applyingRefresh || value is not { } on)
                return;

            _owner.Post(AttributeEdit.Set(Name, MemberValue.Of(on)));
        }
    }

    /// <summary>The colour, as a brush for the swatch. Transparent while the selection disagrees.</summary>
    public IBrush Swatch
    {
        get => _swatch;
        private set => Set(ref _swatch, value);
    }

    /// <summary>The colour in linear light for the picker, or a NaN vector while the selection disagrees.</summary>
    public Vector3 ColorLinear => _color is { } color ? color.ToLinear() : new Vector3(float.NaN);

    /// <summary>Whether only some of the selected nodes have the attribute.</summary>
    public bool IsPartial
    {
        get => _isPartial;
        private set => Set(ref _isPartial, value);
    }

    /// <summary>What a row only some nodes have says under itself.</summary>
    public string PartialNote
    {
        get => _partialNote;
        private set => Set(ref _partialNote, value);
    }

    /// <summary>
    /// Whether a selected node's entity has a setting of this name. Scripts
    /// then read the setting and never this attribute.
    /// </summary>
    public bool IsTakenByEntity
    {
        get => _isTakenByEntity;
        private set => Set(ref _isTakenByEntity, value);
    }

    /// <summary>What a row says when the entity has a setting of its name.</summary>
    public string TakenNote =>
        $"The entity has a setting called '{Name}', and scripts read that. Give this attribute another name.";

    /// <summary>The first refusal a field of the row shows, or empty.</summary>
    public string Rejection
    {
        get
        {
            foreach (PropertyFieldModel cell in AllFields())
            {
                if (cell.HasRejection) return cell.Rejection;
            }

            return string.Empty;
        }
    }

    /// <summary>Whether a field of the row is showing a refusal.</summary>
    public bool HasRejection => Rejection.Length > 0;

    internal void Refresh(in AttributePanelRow row, int selectionCount)
    {
        IsPartial = row.Count < selectionCount;
        PartialNote = IsPartial
            ? $"On {row.Count} of {selectionCount}. A new value goes on all {selectionCount}."
            : string.Empty;
        IsTakenByEntity = row.IsTakenByEntity;
        NameField.Refresh(row.Name, mixed: false);

        MemberValue value = row.Value;
        switch (Kind)
        {
            case MemberValueKind.Text:
                Cells[0].Refresh(value.Text, row.IsMixed);
                break;

            case MemberValueKind.Number:
                Cells[0].Refresh(FormatNumber(value.Number), row.IsMixed);
                break;

            case MemberValueKind.Boolean:
                _applyingRefresh = true;
                Flag = row.IsMixed ? null : value.Boolean;
                _applyingRefresh = false;
                break;

            case MemberValueKind.Color:
                RefreshColor(row.IsMixed ? null : value.Color);
                break;

            case MemberValueKind.Vector3 or MemberValueKind.CFrame:
                RefreshParts(in value, row.MixedParts);
                break;
        }
    }

    // Writes a colour from the picker while it is dragged. The swatch is
    // refreshed here and does not wait for the engine to say it back.
    internal void ScrubColor(Vector3 linear)
    {
        if (!IsColor) return;

        Color3 color = Color3.FromLinear(linear);
        Cells[0].SetScrubText(HexOf(color));
        ShowSwatch(color);
        _owner.Post(AttributeEdit.Set(Name, MemberValue.Of(color)));
    }

    private void RefreshParts(in MemberValue value, int mixedParts)
    {
        for (int part = 0; part < Cells.Count; part++)
            Cells[part].Refresh(FormatPart(AttributeParts.Get(in value, part)), (mixedParts & (1 << part)) != 0);

        // Rounded as a node's own Rotation row is: the angles come out of a
        // quaternion and carry its rounding.
        for (int part = 0; part < TurnCells.Count; part++)
        {
            TurnCells[part].Refresh(
                PropertyFieldModel.Format(AttributeParts.Get(in value, part + 3)), (mixedParts & (8 << part)) != 0);
        }
    }

    private void RefreshColor(Color3? color)
    {
        if (color != _color || ReferenceEquals(_swatch, Brushes.Transparent))
            ShowSwatch(color);

        Cells[0].Refresh(color is { } shown ? HexOf(shown) : string.Empty, color is null);
    }

    private void ShowSwatch(Color3? color)
    {
        _color = color;
        Swatch = color is { } shown
            ? new SolidColorBrush(Color.FromRgb(ToByte(shown.R), ToByte(shown.G), ToByte(shown.B)))
            : Brushes.Transparent;
    }

    private void CommitName(PropertyFieldModel field, string typed)
    {
        if (_owner.NameRefusal(typed, isNew: false) is { } whyNot)
        {
            field.Reject($"Not renamed: {whyNot}");
            return;
        }

        _owner.Post(AttributeEdit.Rename(Name, typed));
    }

    private void CommitWhole(PropertyFieldModel field, string typed)
    {
        MemberValue value;
        switch (Kind)
        {
            case MemberValueKind.Number:
                if (!TryParseNumber(typed, out double number))
                {
                    field.Reject("Not applied: expected a number.");
                    return;
                }

                value = MemberValue.Of(number);
                break;

            case MemberValueKind.Color:
                if (!Color3.TryFromHex(typed, out Color3 color))
                {
                    field.Reject("Not applied: expected #RRGGBB.");
                    return;
                }

                value = MemberValue.Of(color);
                break;

            default:
                value = MemberValue.Of(typed);
                break;
        }

        if (PropertyLimits.AttributeValueRefusal(in value) is { } whyNot)
        {
            field.Reject($"Not applied: {whyNot}");
            return;
        }

        _owner.Post(AttributeEdit.Set(Name, value));
    }

    // One number per cell: typing into y leaves each node's x and z alone.
    private void CommitPart(PropertyFieldModel field, string typed, int part)
    {
        if (!PropertyFieldModel.TryParseNumber(typed, out float number))
        {
            field.Reject($"Not applied: expected a number for {field.Label}.");
            return;
        }

        _owner.Post(AttributeEdit.SetPart(Name, AttributeParts.DefaultOf(Kind), part, number));
    }

    private PropertyFieldModel[] PartCells(int first)
    {
        var cells = new PropertyFieldModel[3];
        for (int i = 0; i < cells.Length; i++)
        {
            int part = first + i;
            cells[i] = new PropertyFieldModel(
                PropertyId.None, Axes[i], AxisLabels[i], (field, typed) => CommitPart(field, typed, part));
        }

        return cells;
    }

    private static PropertyFieldModel Cell(Action<PropertyFieldModel, string> commit, bool allowsEmpty) =>
        new(PropertyId.None, PropertyAxes.All, string.Empty, commit) { AllowsEmpty = allowsEmpty };

    private IEnumerable<PropertyFieldModel> AllFields()
    {
        yield return NameField;
        foreach (PropertyFieldModel cell in Cells) yield return cell;
        foreach (PropertyFieldModel cell in TurnCells) yield return cell;
    }

    // Written so that it reads back as the same number: a script gets what the cell shows.
    private static string FormatNumber(double number) => number.ToString("R", CultureInfo.InvariantCulture);

    private static string FormatPart(float number) => number.ToString("R", CultureInfo.InvariantCulture);

    private static bool TryParseNumber(string text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    private static string HexOf(Color3 color) => $"#{ToByte(color.R):X2}{ToByte(color.G):X2}{ToByte(color.B):X2}";

    private static byte ToByte(float part) =>
        (byte)Math.Clamp(MathF.Round((float.IsFinite(part) ? part : 0f) * 255f), 0f, 255f);
}
