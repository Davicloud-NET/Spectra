using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// One wire in the Sends section: which output fires it, what it sends and to
/// whom.
/// </summary>
// Every commit posts the whole wire list; the command carries absolute arrays.
public sealed class ConnectionRowModel : ObservableObject
{
    /// <summary>What an unlimited fire count is called on screen. The file still stores -1.</summary>
    public const string ForeverLabel = "Forever";

    private readonly Action _changed;
    private readonly WireEnds _ends = new();
    private string _output = string.Empty;
    private string _target = string.Empty;
    private string _input = string.Empty;
    private string _parameter = string.Empty;
    private string _parameterWarning = string.Empty;
    private float _delay;
    private int _times = EntityConnection.Infinite;
    private bool _targetResolves = true;
    private bool _applyingRefresh;
    private IReadOnlyList<string> _outputChoices = [];
    private IReadOnlyList<string> _inputChoices = [];

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
            Edited();
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
            Edited();
        }
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

    /// <summary>Whether to show the amber line about the parameter under this wire.</summary>
    public bool HasParameterWarning => _parameterWarning.Length > 0;

    /// <summary>
    /// Why the parameter will not do what it looks like it does, in words,
    /// or empty. A warning only: the wire is kept and saved as typed.
    /// </summary>
    public string ParameterWarning
    {
        get => _parameterWarning;
        private set
        {
            if (Set(ref _parameterWarning, value))
                Raise(nameof(HasParameterWarning));
        }
    }

    // The class of the entity the wire leaves, when the session knows it.
    internal EntitySchema? Sender
    {
        get => _ends.Sender;
        set => _ends.Sender = value;
    }

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

        _ends.Look(targets, schemas);
        RefreshInputs();
        JudgeParameter();
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

    // Finds who the target names now, and offers that class's inputs.
    private void RefreshInputs()
    {
        _ends.Aim(_target);

        IReadOnlyList<string> inputs = _ends.Receiver?.InputNames ?? [];

        bool wasPickable = HasInputChoices;

        if (!ReferenceEquals(_inputChoices, inputs))
        {
            _inputChoices = inputs;
            Raise(nameof(InputChoices));
        }

        if (wasPickable != HasInputChoices)
            Raise(nameof(HasInputChoices));
    }

    private void JudgeParameter() => ParameterWarning = _ends.ParameterWarning(ToConnection());

    // The end of every edit that can change what the parameter means.
    private void Edited()
    {
        JudgeParameter();
        _changed();
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
        Edited();
    }

    private void CommitTarget(PropertyFieldModel field, string typed)
    {
        _target = typed;
        Raise(nameof(TargetWarning));

        // Recompute the input list now rather than on the next publish.
        RefreshInputs();
        Edited();
    }

    private void CommitInput(PropertyFieldModel field, string typed)
    {
        if (_input == typed) return;

        _input = typed;
        Raise(nameof(Input));

        Raise(nameof(HasInputChoices));
        Edited();
    }

    private void CommitParameter(PropertyFieldModel field, string typed)
    {
        _parameter = typed;
        Edited();
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
}
