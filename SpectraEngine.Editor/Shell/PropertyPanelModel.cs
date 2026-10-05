using Avalonia.Media;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editing.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Editor.Shell;

/// <summary>One row of the property panel: a label, a unit, and its editor.</summary>
public sealed class PropertyRowModel : ObservableObject
{
    private bool _isPartial;
    private int _presentCount;
    private int _selectionCount;
    private bool _flag;
    private string _choice = string.Empty;
    private bool _applyingRefresh;
    private Vector3 _color;
    private string _assetPath = string.Empty;
    private bool _assetMixed;
    private string _note = string.Empty;
    private string _hex = string.Empty;
    private IBrush _swatch = Brushes.Transparent;

    internal PropertyRowModel(PropertyRow row, Action<PropertyEdit> apply)
    {
        Id = row.Id;
        Key = row.Key ?? string.Empty;
        Group = row.Group;
        Name = row.Name;
        Kind = row.Kind;
        Unit = row.Unit ?? string.Empty;
        Choices = row.Choices;
        ChoiceLabels = row.ChoiceLabels ?? row.Choices;
        AssetKind = row.Asset;
        Help = row.Help ?? string.Empty;

        // Labels pair with choices by index, so the counts must match.
        if (ChoiceLabels.Count != Choices.Count)
        {
            throw new InvalidOperationException(
                $"Row '{row.Name}' has {Choices.Count} choices and {ChoiceLabels.Count} labels.");
        }
        Apply = apply;

        Fields = Kind switch
        {
            // One cell holding a hex string. A real field, not a bound string:
            // parsed per keystroke, "#8" never gets as far as "#808080".
            PropertyKind.Color =>
                [new PropertyFieldModel(Id, PropertyAxes.All, string.Empty, CommitField)],

            PropertyKind.Vector3 =>
            [
                new PropertyFieldModel(Id, PropertyAxes.X, "x", CommitField),
                new PropertyFieldModel(Id, PropertyAxes.Y, "y", CommitField),
                new PropertyFieldModel(Id, PropertyAxes.Z, "z", CommitField),
            ],
            PropertyKind.Number or PropertyKind.Text or PropertyKind.Target =>
                [new PropertyFieldModel(Id, PropertyAxes.All, string.Empty, CommitField)
                    { Unit = row.Unit ?? string.Empty }],
            _ => [],
        };

        // Re-raise the cells' refusals so the line under the row binds to the row.
        foreach (PropertyFieldModel field in Fields)
        {
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(PropertyFieldModel.Rejection)
                    or nameof(PropertyFieldModel.HasRejection))
                {
                    Raise(nameof(Rejection));
                    Raise(nameof(HasRejection));
                }
            };
        }
    }

    public PropertyId Id { get; }

    /// <summary>
    /// Which keyvalue this row is, for the ids whose <see cref="Id"/> alone
    /// does not name one. Empty on every other row. Every edit carries both.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Whether this row edits an entity keyvalue, whose value is committed as
    /// wire text whatever widget the schema asked for.
    /// </summary>
    public bool IsEntityKeyvalue => Id == PropertyId.EntityKeyvalue;

    // Index in the published list. The panel skips some published rows (the
    // header shows them), so model rows and snapshot rows don't line up.
    internal int SourceIndex { get; set; }

    public string Group { get; }
    public string Name { get; }
    public PropertyKind Kind { get; }
    public IReadOnlyList<string> Choices { get; }

    /// <summary>The words shown for <see cref="Choices"/>, index for index.</summary>
    public IReadOnlyList<string> ChoiceLabels { get; }

    /// <summary>One line explaining the choices, for the row's tooltip.</summary>
    public string Help { get; }

    /// <summary>Whether there is anything to explain.</summary>
    public bool HasHelp => Help.Length > 0;

    /// <summary>
    /// The dropdown's value: the display word for <see cref="Choice"/>. Setting
    /// it commits the matching token; an unlisted word is taken as a token.
    /// </summary>
    public string ChoiceLabel
    {
        get
        {
            int index = IndexOfChoice(_choice);
            return index >= 0 ? ChoiceLabels[index] : _choice;
        }

        set
        {
            if (_applyingRefresh || string.IsNullOrEmpty(value)) return;

            int index = IndexOfLabel(value);
            Choice = index >= 0 ? Choices[index] : value;
        }
    }

    private int IndexOfChoice(string value)
    {
        for (int i = 0; i < Choices.Count; i++)
        {
            if (string.Equals(Choices[i], value, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    private int IndexOfLabel(string label)
    {
        for (int i = 0; i < ChoiceLabels.Count; i++)
        {
            if (string.Equals(ChoiceLabels[i], label, StringComparison.Ordinal)) return i;
        }

        return -1;
    }
    public IReadOnlyList<PropertyFieldModel> Fields { get; }

    /// <summary>The first cell's refusal, or empty. Shown as one line under the row.</summary>
    public string Rejection
    {
        get
        {
            foreach (PropertyFieldModel cell in Fields)
            {
                if (cell.HasRejection) return cell.Rejection;
            }

            return string.Empty;
        }
    }

    /// <summary>Whether any cell in this row is showing a refusal.</summary>
    public bool HasRejection => Rejection.Length > 0;

    /// <summary>What this row accepts, for the message when it did not.</summary>
    public string Expected => PropertyLimits.Expected(Id, Kind);

    /// <summary>
    /// This row's colour in linear light, or a NaN vector when the selection
    /// disagrees.
    /// </summary>
    public Vector3 ColorLinear => _color;

    /// <summary>Whether the selection disagrees about this colour.</summary>
    public bool IsColorMixed => float.IsNaN(_color.X);

    // Writes a colour from the picker. Goes through the scrub guard like a
    // numeric drag, and refreshes the swatch here instead of waiting for the echo.
    internal void ScrubColor(Vector3 linear)
    {
        if (Kind != PropertyKind.Color || Fields.Count == 0) return;

        Fields[0].SetScrubText(ToHex(linear));
        RefreshColor(linear);

        Apply(IsEntityKeyvalue
            ? new PropertyEdit
            {
                Id = Id, Key = Key, Axes = PropertyAxes.All,
                Text = KeyvalueWire.FormatColor(linear),
            }
            : new PropertyEdit { Id = Id, Axes = PropertyAxes.All, Vector = linear });
    }

    /// <summary>The unit the value is measured in, or empty.</summary>
    public string Unit { get; }

    /// <summary>Whether there is a unit to print beside the label.</summary>
    public bool HasUnit => Unit.Length > 0;

    internal Action<PropertyEdit> Apply { get; }

    /// <summary>A three-number value, which the panel lays out over two lines.</summary>
    public bool IsVector => Kind == PropertyKind.Vector3;

    /// <summary>A colour, shown as a swatch and a hex value.</summary>
    public bool IsColor => Kind == PropertyKind.Color;

    /// <summary>A one-cell value that fits beside its label.</summary>
    public bool IsScalar => Kind is PropertyKind.Number or PropertyKind.Text;

    /// <summary>
    /// Whether the target picker can open. It needs a single entity selected:
    /// a multi-selection publishes no target list.
    /// </summary>
    public bool CanPickTarget => IsTarget && !IsPartial && _selectionCount <= 1;

    // Each cell's value when a drag began. A label drag applies one delta to
    // all three, and the commands are absolute.
    internal float[] ScrubStarts { get; } = new float[3];

    public bool IsBoolean => Kind == PropertyKind.Boolean;
    public bool IsChoice => Kind == PropertyKind.Choice;
    public bool IsReadOnly => Kind == PropertyKind.ReadOnlyText;

    /// <summary>A file chosen from the project, shown as a name and a button.</summary>
    public bool IsAsset => Kind == PropertyKind.Asset;

    /// <summary>An entity name, typed or picked.</summary>
    public bool IsTarget => Kind == PropertyKind.Target;

    /// <summary>Writes a picked entity name through the field's commit path.</summary>
    public void PickTarget(string name)
    {
        if (!IsTarget || Fields.Count == 0 || string.IsNullOrEmpty(name)) return;

        PropertyFieldModel field = Fields[0];
        // BeginEdit first: Commit returns early without an open edit.
        // Not SetScrubText: it also sets the live value, so Commit sees no change.
        field.BeginEdit();
        field.Text = name;
        field.Commit();
    }

    /// <summary>Which kind of file this row's picker offers.</summary>
    public AssetKind AssetKind { get; }

    /// <summary>The content-relative path this row holds, empty for the engine default.</summary>
    public string AssetPath
    {
        get => _assetPath;
        private set
        {
            if (!Set(ref _assetPath, value))
                return;

            Raise(nameof(AssetLabel));
            Raise(nameof(CanPreview));
        }
    }

    /// <summary>Whether the row holds one sound file, which its play button can play.</summary>
    public bool CanPreview => AssetKind == AssetKind.Sound && _assetPath.Length > 0;

    /// <summary>The file's stem, or "(mixed)", or what an empty row means for its kind.</summary>
    public string AssetLabel
    {
        get
        {
            if (IsPartial || _assetMixed) return "(mixed)";

            // An empty material is the engine's default one. No sound stands in for an empty sound.
            if (_assetPath.Length == 0) return AssetKind == AssetKind.Sound ? "(none)" : "(default)";

            int slash = _assetPath.LastIndexOf('/');
            string name = slash >= 0 ? _assetPath[(slash + 1)..] : _assetPath;
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name[..dot] : name;
        }
    }

    /// <summary>
    /// What is wrong or unusual about this row's value, in one or two words,
    /// such as "missing" for a material whose file does not exist.
    /// </summary>
    public string Note
    {
        get => _note;
        private set
        {
            if (Set(ref _note, value))
                Raise(nameof(HasNote));
        }
    }

    /// <summary>Whether there is a note to show.</summary>
    public bool HasNote => _note.Length > 0;

    /// <summary>Writes a picked file into this row. An empty path means the engine default.</summary>
    public void PickAsset(string contentPath)
    {
        if (!IsAsset) return;

        Apply(new PropertyEdit { Id = Id, Key = Key, Text = contentPath ?? string.Empty });
    }

    /// <summary>Whether this row's label is a drag handle for its value.</summary>
    public bool IsScrubbable => Kind is PropertyKind.Number or PropertyKind.Vector3;

    /// <summary>The value, for a read-only row.</summary>
    public string ReadOnlyText { get; private set; } = string.Empty;

    /// <summary>
    /// How much one pixel of horizontal drag is worth, in the value's own unit.
    /// </summary>
    // Tuned so a 200px drag is about 4 units of position, 50 degrees, or one
    // doubling of scale.
    public float ScrubStep => Id switch
    {
        PropertyId.Rotation => 0.25f,
        PropertyId.Scale => 0.005f,
        PropertyId.LightIntensity => 0.05f,
        PropertyId.LightRange => 0.05f,

        // Face texture values are small numbers: repeats, units per repeat.
        PropertyId.FaceUScale or PropertyId.FaceVScale => 0.01f,
        PropertyId.FaceUOffset or PropertyId.FaceVOffset => 0.005f,
        PropertyId.FaceRotation => 0.25f,
        _ => 0.02f,
    };

    /// <summary>How much one arrow-key press is worth.</summary>
    public float KeyStep => Id switch
    {
        PropertyId.Rotation => 5f,
        PropertyId.FaceRotation => 5f,
        PropertyId.Scale => 0.1f,
        PropertyId.FaceUScale or PropertyId.FaceVScale => 0.1f,
        PropertyId.FaceUOffset or PropertyId.FaceVOffset => 0.05f,
        _ => 1f,
    };

    /// <summary>The colour, as a brush for the swatch.</summary>
    public IBrush Swatch
    {
        get => _swatch;
        private set => Set(ref _swatch, value);
    }

    /// <summary>
    /// The colour as an sRGB hex string, for the swatch's tooltip. The stored
    /// value is linear. Read-only: edits go through <c>Fields[0]</c>.
    /// </summary>
    public string Hex
    {
        get => _hex;
        private set => Set(ref _hex, value);
    }

    /// <summary>Whether the selection disagrees about this row's value.</summary>
    public bool IsPartial
    {
        get => _isPartial;
        private set
        {
            if (Set(ref _isPartial, value))
                Raise(nameof(PartialLabel));
        }
    }

    /// <summary>"3 of 5" when the property is unique to part of the selection.</summary>
    public string PartialLabel => _isPartial
        ? string.Format(CultureInfo.InvariantCulture, "{0} of {1}", _presentCount, _selectionCount)
        : string.Empty;

    /// <summary>The value, for a checkbox row.</summary>
    public bool Flag
    {
        get => _flag;
        set
        {
            if (!Set(ref _flag, value) || _applyingRefresh)
                return;

            Apply(IsEntityKeyvalue
                ? new PropertyEdit { Id = Id, Key = Key, Text = KeyvalueWire.Format(value) }
                : new PropertyEdit { Id = Id, Flag = value });
        }
    }

    /// <summary>The value, for a choice row.</summary>
    public string Choice
    {
        get => _choice;
        set
        {
            if (!Set(ref _choice, value) || _applyingRefresh || string.IsNullOrEmpty(value))
                return;

            // The value is already the wire token, so an entity choice needs only its key.
            Apply(new PropertyEdit { Id = Id, Key = Key, Text = value });
        }
    }

    internal void Refresh(PropertyRow row)
    {
        // The IsPartial setter raises PartialLabel when the flag flips. Raise
        // for the counts only when they moved: this runs per row per pump.
        bool partialCountsChanged =
            _presentCount != row.PresentCount || _selectionCount != row.SelectionCount;
        IsPartial = row.IsPartial;
        _presentCount = row.PresentCount;
        _selectionCount = row.SelectionCount;
        if (partialCountsChanged)
            Raise(nameof(PartialLabel));

        switch (Kind)
        {
            case PropertyKind.Vector3:
                Fields[0].Refresh(PropertyFieldModel.Format(row.Vector.X), row.MixedAxes.HasFlag(PropertyAxes.X));
                Fields[1].Refresh(PropertyFieldModel.Format(row.Vector.Y), row.MixedAxes.HasFlag(PropertyAxes.Y));
                Fields[2].Refresh(PropertyFieldModel.Format(row.Vector.Z), row.MixedAxes.HasFlag(PropertyAxes.Z));
                break;

            case PropertyKind.Color:
                RefreshColor(row.IsMixed ? new Vector3(float.NaN) : row.Vector);
                break;

            case PropertyKind.Number:
                Fields[0].Refresh(PropertyFieldModel.Format(row.Number), row.IsMixed);
                break;

            case PropertyKind.Text:
            case PropertyKind.Target:
                Fields[0].Refresh(row.Text, row.IsMixed);
                break;

            case PropertyKind.Boolean:
                // Guarded, or the assignment looks like a click and applies itself.
                _applyingRefresh = true;
                Flag = row.Flag;
                _applyingRefresh = false;
                break;

            case PropertyKind.Asset:
                // No guard needed: nothing here is bound two-way.
                _assetMixed = row.IsMixed;
                AssetPath = row.IsMixed ? string.Empty : row.Text;
                Note = row.Note ?? string.Empty;
                Raise(nameof(AssetLabel));
                break;

            case PropertyKind.Choice:
                _applyingRefresh = true;
                Choice = row.IsMixed ? string.Empty : row.Text;
                _applyingRefresh = false;

                // The dropdown binds to the label, not the token.
                Raise(nameof(ChoiceLabel));
                break;

            default:
                string readOnly = row.IsMixed ? "(multiple)" : row.Text;
                if (!string.Equals(ReadOnlyText, readOnly, StringComparison.Ordinal))
                {
                    ReadOnlyText = readOnly;
                    Raise(nameof(ReadOnlyText));
                }
                break;
        }
    }

    private void RefreshColor(Vector3 linear)
    {
        bool mixed = float.IsNaN(linear.X);

        // Skip the swatch for an unchanged colour: Set compares brushes by
        // reference, so every pump would allocate a new one. NaN != NaN, so
        // mixed is compared as a state.
        bool sameValue = _colorRefreshed &&
            (mixed ? float.IsNaN(_color.X) : !float.IsNaN(_color.X) && _color == linear);

        _colorRefreshed = true;
        _color = linear;
        Hex = mixed ? string.Empty : ToHex(linear);

        if (!sameValue)
        {
            Swatch = mixed
                ? Brushes.Transparent
                : new SolidColorBrush(Color.FromRgb(ToByte(linear.X), ToByte(linear.Y), ToByte(linear.Z)));
        }

        // The field refreshes every pump, outside the sameValue guard. That
        // is what puts the real value back after a refused commit.
        Fields[0].Refresh(Hex, mixed);
    }

    // The first refresh must never be skipped, or a black light gets no swatch.
    private bool _colorRefreshed;

    // Writes one absolute value while a drag is in flight.
    internal void ScrubTo(PropertyFieldModel field, float value)
    {
        field.SetScrubText(PropertyFieldModel.Format(value));

        if (Kind == PropertyKind.Number)
        {
            Apply(IsEntityKeyvalue
                ? new PropertyEdit { Id = Id, Key = Key, Text = KeyvalueWire.Format(value) }
                : new PropertyEdit { Id = Id, Number = value });
            return;
        }

        Apply(IsEntityKeyvalue
            ? new PropertyEdit { Id = Id, Key = Key, Axes = field.Axis, Text = WireTriple(value) }
            : new PropertyEdit
            {
                Id = Id,
                Axes = field.Axis,
                Vector = new Vector3(value, value, value),
            });
    }

    // A per-axis entity edit sends all three components. PropertyEditor
    // splices the masked one by token and writes anything that is not three
    // parts whole, which would replace the vector with one number.
    private static string WireTriple(float value) =>
        KeyvalueWire.Format(new Vector3(value, value, value));

    private void CommitField(PropertyFieldModel field, string typed)
    {
        switch (Kind)
        {
            case PropertyKind.Text:
                Apply(new PropertyEdit { Id = Id, Key = Key, Text = typed });
                break;

            case PropertyKind.Number:
                if (!PropertyFieldModel.TryParseNumber(typed, out float number))
                {
                    field.Reject($"Not applied: expected {Expected}.");
                    return;
                }

                // Refuse here with the editor's own rule. Posted, a bad value is
                // dropped on the render thread and nothing says why.
                if (!IsEntityKeyvalue && PropertyLimits.Refusal(Id, number) is { } why)
                {
                    field.Reject($"Not applied: {Name} must be {why}.");
                    return;
                }

                Apply(IsEntityKeyvalue
                    ? new PropertyEdit { Id = Id, Key = Key, Text = KeyvalueWire.Format(number) }
                    : new PropertyEdit { Id = Id, Number = number });
                break;

            case PropertyKind.Color:
                if (!TryParseHex(typed, out Vector3 rgb))
                {
                    field.Reject("Not applied: expected #RRGGBB.");
                    return;
                }
                Apply(IsEntityKeyvalue
                    ? new PropertyEdit
                    {
                        Id = Id, Key = Key, Axes = PropertyAxes.All,
                        Text = KeyvalueWire.FormatColor(rgb),
                    }
                    : new PropertyEdit { Id = Id, Axes = PropertyAxes.All, Vector = rgb });
                break;

            case PropertyKind.Vector3:
                if (!PropertyFieldModel.TryParseNumber(typed, out float component))
                {
                    field.Reject($"Not applied: expected {Expected} for {field.Label}.");
                    return;
                }

                // One axis per cell: typing into y leaves each node's x and z alone.
                Apply(IsEntityKeyvalue
                    ? new PropertyEdit
                    {
                        Id = Id, Key = Key, Axes = field.Axis, Text = WireTriple(component),
                    }
                    : new PropertyEdit
                    {
                        Id = Id,
                        Axes = field.Axis,
                        Vector = new Vector3(component, component, component),
                    });
                break;
        }
    }

    // Linear <-> sRGB. The texture path does this in hardware and has no callable form.

    private static byte ToByte(float linear)
    {
        float v = float.IsFinite(linear) ? Math.Clamp(linear, 0f, 1f) : 0f;
        float s = v <= 0.0031308f ? v * 12.92f : (1.055f * MathF.Pow(v, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp(MathF.Round(s * 255f), 0f, 255f);
    }

    private static float FromByte(byte value)
    {
        float s = value / 255f;
        return s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
    }

    private static string ToHex(Vector3 linear) =>
        $"#{ToByte(linear.X):X2}{ToByte(linear.Y):X2}{ToByte(linear.Z):X2}";

    // Reads "#RRGGBB" or "RRGGBB" into linear RGB.
    internal static bool TryParseHex(string? text, out Vector3 linear)
    {
        linear = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];

        if (span.Length != 6)
            return false;

        if (!byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        linear = new Vector3(FromByte(r), FromByte(g), FromByte(b));
        return true;
    }
}

/// <summary>
/// The property panel: the selection's rows, grouped, patched from each
/// published snapshot.
/// </summary>
// Rows are reused while the set of properties stays the same. A fresh
// collection per snapshot would reset scroll and focus 30 times a second.
// Sections are runs of equal Group: the inspector emits each group contiguously.
public sealed class PropertyPanelModel : ObservableObject
{
    private readonly Action<PropertyEdit> _apply;
    private readonly Action<string> _beginGesture;
    private readonly Action<bool> _endGesture;
    private readonly PropertyRowShape _shape = new();
    private int _selectionCount;
    private string _headerKind = string.Empty;
    private string _unknownClass = string.Empty;
    private bool _isMultiple;

    /// <param name="apply">Posts one property edit to the editor.</param>
    /// <param name="beginGesture">Opens one history entry around a drag.</param>
    /// <param name="endGesture">Closes it, keeping the result or rolling it back.</param>
    /// <param name="applyConnections">
    /// Posts a whole replacement wiring list for one node, addressed by node
    /// id rather than by selection. Null in a host that offers no wiring.
    /// </param>
    public PropertyPanelModel(
        Action<PropertyEdit> apply,
        Action<string> beginGesture,
        Action<bool> endGesture,
        Action<Guid, IReadOnlyList<EntityConnection>>? applyConnections = null)
    {
        _apply = apply;
        _beginGesture = beginGesture;
        _endGesture = endGesture;
        Wiring = new EntityWiringModel(applyConnections ?? ((_, _) => { }));

        NameField = new PropertyFieldModel(
            PropertyId.NodeName, PropertyAxes.All, string.Empty,
            (field, typed) => _apply(new PropertyEdit { Id = PropertyId.NodeName, Text = typed }));
    }

    /// <summary>The node's name, edited in the panel's own header.</summary>
    public PropertyFieldModel NameField { get; }

    /// <summary>The rows, in display order, with group headers folded in.</summary>
    public ObservableCollection<PropertyGroupModel> Groups { get; } = [];

    /// <summary>The Sends section: the selected entity's wires.</summary>
    // Not a PropertyGroupModel: a wire has no (id, key) identity and is
    // edited as a whole list.
    public EntityWiringModel Wiring { get; }

    /// <summary>The Receives section: the wires that arrive at the selected entity.</summary>
    public EntityArrivalsModel Arrivals { get; } = new();

    /// <summary>The Now section: the selected entity's state while the level runs.</summary>
    public EntityLiveStateModel LiveState { get; } = new();

    /// <summary>Whether anything is selected at all.</summary>
    public bool HasSelection => _selectionCount > 0;

    /// <summary>Whether the header has a kind to print.</summary>
    public bool HasKind => _headerKind.Length > 0;

    /// <summary>
    /// Whether more than one object is selected, so the header shows a count
    /// instead of an editable name.
    /// </summary>
    public bool IsMultiple
    {
        get => _isMultiple;
        private set
        {
            if (Set(ref _isMultiple, value))
                Raise(nameof(MultipleLabel));
        }
    }

    /// <summary>"3 objects", for a multi-selection header.</summary>
    public string MultipleLabel => $"{_selectionCount} objects";

    /// <summary>What kind of thing it is, derived from which sections exist.</summary>
    public string HeaderKind
    {
        get => _headerKind;
        private set
        {
            if (Set(ref _headerKind, value))
                Raise(nameof(HasKind));
        }
    }

    /// <summary>Whether the selection names an entity class nothing declares.</summary>
    public bool HasUnknownClass => _unknownClass.Length > 0;

    /// <summary>
    /// The standing warning shown for an entity whose class this session has no
    /// schema for. Not an error: the authored keyvalues stay editable as text.
    /// </summary>
    public string UnknownClassLabel
    {
        get => _unknownClass;
        private set
        {
            if (Set(ref _unknownClass, value))
                Raise(nameof(HasUnknownClass));
        }
    }

    /// <summary>
    /// The entity classes the session knows, or null when none is open. The
    /// same catalogue the Insert menu is built from. Set when a session starts.
    /// </summary>
    public EntitySchemaCatalog? Schemas
    {
        get => _schemas;
        set
        {
            _schemas = value;
            Wiring.Schemas = value;
        }
    }

    private EntitySchemaCatalog? _schemas;

    // Opens one history entry around a drag.
    internal void BeginGesture(string name) => _beginGesture(name);

    // Closes it, keeping the result or rolling it back.
    internal void EndGesture(bool commit) => _endGesture(commit);

    /// <summary>Takes one published snapshot's rows.</summary>
    /// <param name="rows">The selection's merged property rows.</param>
    /// <param name="selectionCount">How many nodes are selected.</param>
    /// <param name="entity">
    /// The selected entity's class and wiring, or null when the selection is
    /// not exactly one node carrying an entity.
    /// </param>
    public void Apply(
        IReadOnlyList<PropertyRow> rows, int selectionCount, EntityPanelInfo? entity = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        Wiring.Apply(entity);
        Arrivals.Apply(entity);
        LiveState.Apply(entity);

        if (_selectionCount != selectionCount)
        {
            _selectionCount = selectionCount;
            Raise(nameof(HasSelection));
            // The IsMultiple setter only raises this when the flag flips.
            Raise(nameof(MultipleLabel));
        }

        if (!_shape.Matches(rows))
            Rebuild(rows);

        foreach (PropertyGroupModel group in Groups)
        {
            foreach (PropertyRowModel row in group.Rows)
                row.Refresh(rows[row.SourceIndex]);
        }

        RefreshHeader(rows, selectionCount);
    }

    private void RefreshHeader(IReadOnlyList<PropertyRow> rows, int selectionCount)
    {
        IsMultiple = selectionCount > 1;

        if (selectionCount == 0)
        {
            HeaderKind = string.Empty;
            UnknownClassLabel = string.Empty;
            return;
        }

        string name = string.Empty;
        bool nameMixed = false;
        string brushKind = string.Empty;
        string brushOperation = string.Empty;
        string className = string.Empty;
        bool classMixed = false;
        bool hasLight = false;
        bool hasMesh = false;
        bool hasBrush = false;
        bool hasEntity = false;

        foreach (PropertyRow row in rows)
        {
            switch (row.Id)
            {
                case PropertyId.NodeName:
                    name = row.Text;
                    nameMixed = row.IsMixed;
                    break;
                case PropertyId.BrushKind:
                    hasBrush = true;
                    brushKind = row.IsMixed ? string.Empty : row.Text;
                    break;
                case PropertyId.BrushOperation:
                    brushOperation = row.IsMixed ? string.Empty : row.Text;
                    break;
                case PropertyId.LightKind:
                    hasLight = true;
                    break;
                case PropertyId.MeshModel:
                    hasMesh = true;
                    break;
                case PropertyId.EntityClassname:
                    hasEntity = true;
                    className = row.Text;
                    classMixed = row.IsMixed;
                    break;
            }
        }

        NameField.Refresh(name, nameMixed);

        HeaderKind = DescribeKind(hasBrush, brushKind, brushOperation, hasEntity, hasLight, hasMesh);

        // No badge for a mixed classname: it would be wrong for part of the selection.
        UnknownClassLabel =
            hasEntity && !classMixed && className.Length > 0 && !Knows(className)
                ? $"Unknown class '{className}' - properties preserved as text"
                : string.Empty;
    }

    // No catalogue means no session yet, so nothing is badged as unknown.
    private bool Knows(string className) =>
        Schemas is null || Schemas.TryGetSchema(className, out _);

    private static string DescribeKind(
        bool hasBrush, string kind, string operation, bool hasEntity, bool hasLight, bool hasMesh)
    {
        if (hasBrush)
        {
            // Subtractive outranks the kind, as in the tree.
            if (operation.Equals("Subtractive", StringComparison.OrdinalIgnoreCase))
                return "Cut";

            return kind switch
            {
                "World" => "Block",
                "Part" => "Part",
                _ => "Brush",
            };
        }

        // Same order as SceneNodeClassifier, so the chip matches the tree row.
        if (hasEntity) return "Entity";
        if (hasLight) return "Light";
        if (hasMesh) return "Mesh";
        return string.Empty;
    }

    // _shape records the published rows, not the built ones: the panel skips
    // some, so comparing against built rows would rebuild on every publish.
    private void Rebuild(IReadOnlyList<PropertyRow> rows)
    {
        Groups.Clear();
        _shape.Clear();

        PropertyGroupModel? current = null;
        for (int i = 0; i < rows.Count; i++)
        {
            PropertyRow row = rows[i];
            _shape.Add(row);

            // Name and id belong to the header, not a section.
            if (row.Id is PropertyId.NodeId or PropertyId.NodeName)
                continue;

            if (current is null || current.Name != row.Group)
            {
                current = new PropertyGroupModel(row.Group);
                Groups.Add(current);
            }

            current.Rows.Add(new PropertyRowModel(row, _apply) { SourceIndex = i });
        }
    }
}

/// <summary>One section of the panel, named after the payload it came from.</summary>
public sealed class PropertyGroupModel
{
    public PropertyGroupModel(string name) => Name = name;

    /// <summary>The section's heading, or empty for the leading unheaded run.</summary>
    public string Name { get; }

    /// <summary>Whether this section prints a heading at all.</summary>
    public bool HasName => Name.Length > 0;

    /// <summary>
    /// The heading in upper case. The panel prints <see cref="Name"/>; this is
    /// kept because tests name it.
    /// </summary>
    public string UpperName => Name.ToUpperInvariant();

    public ObservableCollection<PropertyRowModel> Rows { get; } = [];
}
