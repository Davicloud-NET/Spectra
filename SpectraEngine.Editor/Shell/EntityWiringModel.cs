using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// One wire in the Sends section: which output fires it, what it sends and to
/// whom.
/// </summary>
// Every commit posts the whole wire list; the command carries absolute arrays.
public sealed class ConnectionRowModel : ObservableObject
{
    private readonly Action _changed;
    private string _output = string.Empty;
    private string _target = string.Empty;
    private string _input = string.Empty;
    private string _parameter = string.Empty;
    private float _delay;
    private int _times = EntityConnection.Infinite;
    private bool _targetResolves = true;
    private bool _applyingRefresh;
    private IReadOnlyList<string> _outputChoices = [];

    internal ConnectionRowModel(Action changed)
    {
        _changed = changed;

        // PropertyFieldModel is reused for its commit behaviour; these cells
        // belong to no inspector row, hence PropertyId.None.
        TargetField = new PropertyFieldModel(
            PropertyId.None, PropertyAxes.All, string.Empty, CommitTarget);
        InputField = new PropertyFieldModel(
            PropertyId.None, PropertyAxes.All, string.Empty, CommitInput);

        // Empty is a real value here: send no argument.
        ParameterField = new PropertyFieldModel(
            PropertyId.None, PropertyAxes.All, string.Empty, CommitParameter)
        { AllowsEmpty = true };

        DelayField = new PropertyFieldModel(
            PropertyId.None, PropertyAxes.All, string.Empty, CommitDelay) { Unit = "s" };
        TimesField = new PropertyFieldModel(
            PropertyId.None, PropertyAxes.All, string.Empty, CommitTimes);

        // Text editor for an output the schema does not declare. A field model,
        // because a two-way Text binding would post an edit per keystroke.
        OutputField = new PropertyFieldModel(
            PropertyId.None, PropertyAxes.All, string.Empty, CommitOutput)
        { AllowsEmpty = true };
    }

    /// <summary>Who to send to.</summary>
    public PropertyFieldModel TargetField { get; }

    /// <summary>Which input to send.</summary>
    public PropertyFieldModel InputField { get; }

    /// <summary>The argument to send, empty for none.</summary>
    public PropertyFieldModel ParameterField { get; }

    /// <summary>Seconds to wait before sending.</summary>
    public PropertyFieldModel DelayField { get; }

    /// <summary>How many times this wire may fire, or -1 for forever.</summary>
    public PropertyFieldModel TimesField { get; }

    /// <summary>The output as typed text, for one the class does not declare.</summary>
    public PropertyFieldModel OutputField { get; }

    /// <summary>The output that fires this wire, as the dropdown edits it.</summary>
    // Ignored during a refresh: a published value assigned back looks like a pick.
    // Empty is refused: a ComboBox clears SelectedItem when its item source is
    // replaced, and the binding delivers that here like a click.
    public string Output
    {
        get => _output;
        set
        {
            if (_applyingRefresh || string.IsNullOrEmpty(value))
                return;

            if (!Set(ref _output, value))
                return;

            OutputField.Refresh(value, mixed: false);
            Raise(nameof(HasOutputChoices));
            _changed();
        }
    }

    /// <summary>The outputs the class declares, offered by the dropdown.</summary>
    // Must be the schema's own list instance, never a copy widened by the
    // authored value. Replacing a bound item source makes the ComboBox drop its
    // selection, and the binding will not push the value again.
    public IReadOnlyList<string> OutputChoices
    {
        get => _outputChoices;
        private set
        {
            if (!ReferenceEquals(_outputChoices, value))
            {
                _outputChoices = value;
                Raise();
                Raise(nameof(HasOutputChoices));
            }
        }
    }

    /// <summary>
    /// Whether the class declares this wire's output, so the row shows a
    /// dropdown. Otherwise it shows a text box.
    /// </summary>
    public bool HasOutputChoices => Declares(_outputChoices, _output);

    private IReadOnlyList<EntityTargetInfo> _targets = [];
    private IReadOnlyList<string> _targetChoices = [];
    private IReadOnlyList<string> _inputChoices = [];
    private bool _targetsTruncated;

    /// <summary>
    /// What the target picker offers: the three runtime tokens, then every
    /// entity in the scene.
    /// </summary>
    public IReadOnlyList<string> TargetChoices => _targetChoices;

    /// <summary>Whether the scene has more entities than the picker lists.</summary>
    public bool TargetsTruncated => _targetsTruncated;

    /// <summary>What to say about a capped list, or empty.</summary>
    public string TargetsNote => _targetsTruncated
        ? $"Showing the first {EntityPanelInfo.MaxTargets} entities. Type a name to reach the rest."
        : string.Empty;

    /// <summary>
    /// The inputs the target's class declares. Empty unless the target names
    /// one entity.
    /// </summary>
    // The schema's own list instance, as with OutputChoices.
    public IReadOnlyList<string> InputChoices => _inputChoices;

    /// <summary>Whether the target's class declares this wire's input, so the row shows a dropdown.</summary>
    public bool HasInputChoices => Declares(_inputChoices, _input);

    /// <summary>The input, as the dropdown edits it.</summary>
    // Empty is refused, as with Output.
    public string Input
    {
        get => _input;
        set
        {
            if (_applyingRefresh || string.IsNullOrEmpty(value))
                return;

            if (_input == value) return;

            _input = value;
            Raise();
            InputField.Refresh(value, mixed: false);
            Raise(nameof(HasInputChoices));
            _changed();
        }
    }

    /// <summary>Writes a picked target through the field's own commit path.</summary>
    public void PickTarget(string name)
    {
        if (string.IsNullOrEmpty(name)) return;

        PropertyFieldModel field = TargetField;
        // BeginEdit first: Commit returns early without an open edit. Not
        // SetScrubText, which also writes the live value, so the commit would
        // see no change.
        field.BeginEdit();
        field.Text = name;
        field.Commit();
    }

    /// <summary>Whether anything in the scene answers to this wire's target.</summary>
    public bool TargetResolves
    {
        get => _targetResolves;
        private set
        {
            if (Set(ref _targetResolves, value))
            {
                Raise(nameof(HasTargetWarning));
                Raise(nameof(TargetWarning));
            }
        }
    }

    /// <summary>Whether to show the amber warning line under this wire.</summary>
    public bool HasTargetWarning => !_targetResolves;

    /// <summary>
    /// Why the target does not resolve, in words. A warning only: the wire is
    /// kept, since the target may be spawned later.
    /// </summary>
    public string TargetWarning => _targetResolves
        ? string.Empty
        : _target.Length == 0
            ? "No target set"
            : $"Nothing here is named '{_target}'";

    /// <summary>This row as the value the command writes.</summary>
    public EntityConnection ToConnection() =>
        new(_output, _target, _input, _parameter, _delay, _times);

    internal void Refresh(
        EntityConnectionInfo info,
        IReadOnlyList<string> declared,
        IReadOnlyList<EntityTargetInfo> targets,
        bool targetsTruncated,
        EntitySchemaCatalog? schemas)
    {
        EntityConnection wire = info.Wire;

        _target = wire.TargetName;
        _input = wire.Input;
        _parameter = wire.Parameter;
        _delay = wire.Delay;
        _times = wire.TimesToFire;

        // Choices before the value: a dropdown cannot select an item its list
        // does not hold yet. Both inside the guard.
        bool pickable = HasOutputChoices;

        _applyingRefresh = true;
        OutputChoices = declared;
        Set(ref _output, wire.Output, nameof(Output));
        _applyingRefresh = false;

        // Can change without the choices changing, when the output itself did.
        if (pickable != HasOutputChoices)
            Raise(nameof(HasOutputChoices));

        OutputField.Refresh(wire.Output, mixed: false);
        TargetField.Refresh(wire.TargetName, mixed: false);
        InputField.Refresh(wire.Input, mixed: false);
        ParameterField.Refresh(wire.Parameter, mixed: false);
        DelayField.Refresh(PropertyFieldModel.Format(wire.Delay), mixed: false);
        TimesField.Refresh(FormatTimes(wire.TimesToFire), mixed: false);

        TargetResolves = info.TargetResolves;

        RefreshTargets(targets, targetsTruncated);
        RefreshInputs(schemas);
    }

    // Rebuilt only when the list instance changed. The engine reuses it across
    // publishes, and rebuilding each time would reset the dropdown's selection.
    private void RefreshTargets(IReadOnlyList<EntityTargetInfo> targets, bool truncated)
    {
        if (ReferenceEquals(_targets, targets) && _targetsTruncated == truncated) return;

        _targets = targets;
        _targetsTruncated = truncated;

        var choices = new List<string>(targets.Count + 3)
        {
            TargetNameIndex.SelfToken,
            TargetNameIndex.ActivatorToken,
            TargetNameIndex.CallerToken,
        };

        for (int i = 0; i < targets.Count; i++)
            choices.Add(targets[i].Name);

        _targetChoices = choices;
        Raise(nameof(TargetChoices));
        Raise(nameof(TargetsTruncated));
        Raise(nameof(TargetsNote));
    }

    private void RefreshInputs(EntitySchemaCatalog? schemas)
    {
        _schemas = schemas;

        IReadOnlyList<string> inputs = InputsFor(_target, _targets, schemas);

        bool wasPickable = HasInputChoices;

        if (!ReferenceEquals(_inputChoices, inputs))
        {
            _inputChoices = inputs;
            Raise(nameof(InputChoices));
        }

        if (wasPickable != HasInputChoices)
            Raise(nameof(HasInputChoices));
    }

    // The inputs a target's class declares, or an empty list.
    internal static IReadOnlyList<string> InputsFor(
        string target,
        IReadOnlyList<EntityTargetInfo> targets,
        EntitySchemaCatalog? schemas)
    {
        if (schemas is null || string.IsNullOrEmpty(target)) return [];

        // A runtime token or a wildcard has no single class.
        if (target[0] == '!' || target[^1] == '*') return [];

        string? className = null;
        for (int i = 0; i < targets.Count; i++)
        {
            if (!string.Equals(targets[i].Name, target, StringComparison.Ordinal)) continue;

            // Two entities share the name (legal: the wire fires at both).
            if (className is not null) return [];

            className = targets[i].ClassName;
        }

        if (className is null) return [];

        return schemas.TryGetSchema(className, out EntitySchema? schema) ? schema.Inputs : [];
    }

    private static bool Declares(IReadOnlyList<string> declared, string output)
    {
        for (int i = 0; i < declared.Count; i++)
        {
            if (string.Equals(declared[i], output, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void CommitOutput(PropertyFieldModel field, string typed)
    {
        if (!Set(ref _output, typed, nameof(Output)))
            return;

        // A declared name switches the row back to the dropdown.
        Raise(nameof(HasOutputChoices));
        _changed();
    }

    private void CommitTarget(PropertyFieldModel field, string typed)
    {
        _target = typed;
        Raise(nameof(TargetWarning));

        // Recompute the input list now rather than on the next publish.
        RefreshInputs(_schemas);

        _changed();
    }

    private void CommitInput(PropertyFieldModel field, string typed)
    {
        if (_input == typed) return;

        _input = typed;
        Raise(nameof(Input));

        Raise(nameof(HasInputChoices));
        _changed();
    }

    private EntitySchemaCatalog? _schemas;

    private void CommitParameter(PropertyFieldModel field, string typed)
    {
        _parameter = typed;
        _changed();
    }

    private void CommitDelay(PropertyFieldModel field, string typed)
    {
        if (!PropertyFieldModel.TryParseNumber(typed, out float value) || value < 0f)
        {
            field.Reject("Not applied: expected a delay of 0 or more seconds.");
            return;
        }

        _delay = value;
        _changed();
    }

    /// <summary>What an unlimited fire count is called on screen. The file still stores -1.</summary>
    public const string ForeverLabel = "Forever";

    /// <summary>The fire count as it is shown.</summary>
    public static string FormatTimes(int timesToFire) =>
        timesToFire < 0 ? ForeverLabel : timesToFire.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads a typed fire count: a whole number, or the word in any case.</summary>
    public static bool TryParseTimes(string typed, out int timesToFire)
    {
        if (string.Equals(typed?.Trim(), ForeverLabel, StringComparison.OrdinalIgnoreCase))
        {
            timesToFire = EntityConnection.Infinite;
            return true;
        }

        return int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out timesToFire);
    }

    private void CommitTimes(PropertyFieldModel field, string typed)
    {
        if (!TryParseTimes(typed, out int value))
        {
            field.Reject($"Not applied: expected a whole number, or {ForeverLabel}.");
            return;
        }

        // Any negative means infinite; write the canonical -1.
        _times = value < 0 ? EntityConnection.Infinite : value;
        _changed();
    }

    internal void Seed(
        IReadOnlyList<string> declared,
        IReadOnlyList<EntityTargetInfo> targets,
        bool targetsTruncated,
        EntitySchemaCatalog? schemas)
    {
        Refresh(
            new EntityConnectionInfo(
                new EntityConnection(
                    declared.Count > 0 ? declared[0] : string.Empty,
                    string.Empty, string.Empty, string.Empty, 0f, EntityConnection.Infinite),
                TargetResolves: false),
            declared, targets, targetsTruncated, schemas);
    }
}

/// <summary>
/// The Sends section: the wires leaving the selected entity. Present only
/// when one entity is selected. UI thread only.
/// </summary>
// After an edit, stale snapshots are ignored until the engine echoes it, or
// until HoldSnapshots have passed and the engine's list wins (an edit can be
// refused, for example during play mode).
public sealed class EntityWiringModel : ObservableObject
{
    /// <summary>
    /// How many disagreeing snapshots to ignore before the engine wins.
    /// </summary>
    public const int HoldSnapshots = 6;

    private readonly Action<Guid, IReadOnlyList<EntityConnection>> _apply;
    private EntityConnection[]? _pending;
    private int _ticks;
    private Guid _nodeId;
    private bool _hasEntity;
    private bool _isKnown = true;
    private IReadOnlyList<string> _outputs = [];
    private IReadOnlyList<EntityTargetInfo> _targets = [];
    private bool _targetsTruncated;

    /// <summary>Every entity a wire could aim at, for the picker.</summary>
    public IReadOnlyList<EntityTargetInfo> Targets => _targets;

    /// <summary>Whether the scene has more than the list carries.</summary>
    public bool TargetsTruncated => _targetsTruncated;

    /// <summary>What to say about a capped list, or empty.</summary>
    public string TargetsNote => _targetsTruncated
        ? $"This scene has more than {EntityPanelInfo.MaxTargets} entities; the picker shows the first of them."
        : string.Empty;

    /// <summary>The session's schema catalogue, assigned by the owning panel.</summary>
    public EntitySchemaCatalog? Schemas { get; set; }

    internal EntityWiringModel(Action<Guid, IReadOnlyList<EntityConnection>> apply) => _apply = apply;

    /// <summary>The wires, in authored order.</summary>
    public ObservableCollection<ConnectionRowModel> Rows { get; } = [];

    /// <summary>Whether the selection is one node carrying an entity.</summary>
    public bool HasEntity
    {
        get => _hasEntity;
        private set
        {
            if (Set(ref _hasEntity, value))
                Raise(nameof(IsEmpty));
        }
    }

    /// <summary>Whether there is an entity here and it has no wires yet.</summary>
    public bool IsEmpty => _hasEntity && Rows.Count == 0;

    /// <summary>
    /// Whether this session has a schema for the selected class, so the
    /// section can say why the output dropdowns are text boxes.
    /// </summary>
    public bool IsKnown
    {
        get => _isKnown;
        private set
        {
            if (Set(ref _isKnown, value))
                Raise(nameof(ShowsUnknownOutputs));
        }
    }

    /// <summary>Whether to explain that the outputs could not be listed.</summary>
    public bool ShowsUnknownOutputs => _hasEntity && !_isKnown;

    /// <summary>Takes one published snapshot's entity payload.</summary>
    public void Apply(EntityPanelInfo? info)
    {
        if (info is null)
        {
            _pending = null;
            _ticks = 0;
            _nodeId = Guid.Empty;
            HasEntity = false;
            IsKnown = true;
            if (Rows.Count > 0)
            {
                Rows.Clear();
                Raise(nameof(IsEmpty));
            }

            return;
        }

        // A pending edit belongs to the previous node.
        if (info.NodeId != _nodeId)
        {
            _nodeId = info.NodeId;
            _pending = null;
            _ticks = 0;
        }

        HasEntity = true;
        IsKnown = info.IsKnown;
        _outputs = info.Outputs;
        _targets = info.Targets;
        _targetsTruncated = info.TargetsTruncated;

        if (_pending is not null)
        {
            if (Matches(info.Connections, _pending))
            {
                _pending = null;
                _ticks = 0;
            }
            else if (++_ticks < HoldSnapshots)
            {
                // Snapshot predates the edit.
                return;
            }
            else
            {
                _pending = null;
                _ticks = 0;
            }
        }

        SyncRows(info.Connections);
    }

    /// <summary>Adds an empty wire and posts the new list.</summary>
    public void Add()
    {
        if (!_hasEntity)
            return;

        var row = new ConnectionRowModel(Post);
        row.Seed(_outputs, _targets, _targetsTruncated, Schemas);
        Rows.Add(row);
        Raise(nameof(IsEmpty));
        Post();
    }

    /// <summary>Removes one wire and posts the new list.</summary>
    public void Remove(ConnectionRowModel row)
    {
        if (row is null || !Rows.Remove(row))
            return;

        Raise(nameof(IsEmpty));
        Post();
    }

    // Connection order is authored data in map.json: never sort or de-duplicate.
    private void Post()
    {
        if (!_hasEntity)
            return;

        var wires = new EntityConnection[Rows.Count];
        for (int i = 0; i < wires.Length; i++)
            wires[i] = Rows[i].ToConnection();

        _pending = wires;
        _ticks = 0;
        _apply(_nodeId, wires);
    }

    // Patch in place: a fresh collection per publish resets scroll and drops
    // a half-typed value.
    private void SyncRows(IReadOnlyList<EntityConnectionInfo> wires)
    {
        bool countChanged = Rows.Count != wires.Count;

        while (Rows.Count > wires.Count)
            Rows.RemoveAt(Rows.Count - 1);

        while (Rows.Count < wires.Count)
            Rows.Add(new ConnectionRowModel(Post));

        for (int i = 0; i < wires.Count; i++)
            Rows[i].Refresh(wires[i], _outputs, _targets, _targetsTruncated, Schemas);

        if (countChanged)
            Raise(nameof(IsEmpty));
    }

    private static bool Matches(IReadOnlyList<EntityConnectionInfo> reported, EntityConnection[] wanted)
    {
        if (reported.Count != wanted.Length)
            return false;

        for (int i = 0; i < wanted.Length; i++)
        {
            if (reported[i].Wire != wanted[i])
                return false;
        }

        return true;
    }
}
