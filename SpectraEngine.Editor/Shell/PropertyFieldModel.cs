using SpectraEngine.Core.Inspection;
using System;
using System.Globalization;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// One editable cell: a whole row for a scalar, or one axis of a vector.
/// Commits on Enter or blur, reverts on Escape or unparseable text, and takes
/// no refreshes while it is being edited or scrubbed.
/// </summary>
public sealed class PropertyFieldModel : ObservableObject
{
    private readonly Action<PropertyFieldModel, string> _commit;
    private string _text = string.Empty;
    private string _live = string.Empty;
    private bool _isMixed;
    private bool _isEditing;
    private bool _isScrubbing;

    internal PropertyFieldModel(
        PropertyId id, PropertyAxes axis, string label, Action<PropertyFieldModel, string> commit)
    {
        Id = id;
        Axis = axis;
        Label = label;
        _commit = commit;
    }

    /// <summary>Which property this cell belongs to.</summary>
    public PropertyId Id { get; }

    /// <summary>Which axis this cell writes, or <see cref="PropertyAxes.All"/> for a scalar.</summary>
    public PropertyAxes Axis { get; }

    /// <summary>The per-axis label (x, y, z), or empty for a scalar.</summary>
    public string Label { get; }

    /// <summary>Whether this cell has an axis letter to show.</summary>
    public bool HasLabel => Label.Length > 0;

    // Axis flags for XAML class bindings, which cannot compare values.

    /// <summary>Whether this cell edits x.</summary>
    public bool IsX => Axis == PropertyAxes.X;

    /// <summary>Whether this cell edits y.</summary>
    public bool IsY => Axis == PropertyAxes.Y;

    /// <summary>Whether this cell edits z.</summary>
    public bool IsZ => Axis == PropertyAxes.Z;

    /// <summary>The unit to print inside a scalar cell, or empty.</summary>
    // Copied from the row so the cell template never binds up to a parent DataContext.
    public string Unit { get; internal set; } = string.Empty;

    /// <summary>Whether there is a unit to print.</summary>
    public bool HasUnit => Unit.Length > 0;

    // When set, committing an empty box writes the empty value instead of
    // reverting. For a wire's parameter, where empty means "no argument".
    internal bool AllowsEmpty { get; init; }

    /// <summary>What the box shows.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;

            // Typing clears a refusal. A refresh writing the live value back must not.
            if (_isEditing) Rejection = string.Empty;
        }
    }

    private string _rejection = string.Empty;

    /// <summary>Why the last commit was not applied, or empty.</summary>
    public string Rejection
    {
        get => _rejection;
        private set
        {
            if (Set(ref _rejection, value)) Raise(nameof(HasRejection));
        }
    }

    /// <summary>Whether this cell is showing a refusal.</summary>
    public bool HasRejection => _rejection.Length > 0;

    /// <summary>Refuses the typed value, puts the live one back and says why.</summary>
    public void Reject(string reason)
    {
        Revert();
        Rejection = reason;
    }

    /// <summary>
    /// Whether the selection disagrees about this cell, so the box shows
    /// nothing rather than one node's value.
    /// </summary>
    public bool IsMixed
    {
        get => _isMixed;
        private set
        {
            if (Set(ref _isMixed, value))
                Raise(nameof(Placeholder));
        }
    }

    /// <summary>Shown in an empty mixed box.</summary>
    public string Placeholder => _isMixed ? "mixed" : string.Empty;

    /// <summary>Whether somebody is typing here right now.</summary>
    public bool IsEditing => _isEditing;

    /// <summary>Takes a fresh value, unless this cell is being edited.</summary>
    public void Refresh(string live, bool mixed)
    {
        // A refusal clears when the value moves, not on every publish, or the
        // 30Hz refresh would erase it within a frame.
        if (!string.Equals(live, _live, StringComparison.Ordinal))
            Rejection = string.Empty;

        _live = live;
        IsMixed = mixed;

        // Two flags, not one: a vector drag ends by clearing the scrub guard
        // on all three cells, and that must not hand a cell somebody is typing
        // in back to the refresh.
        if (_isEditing || _isScrubbing)
            return;

        Text = mixed ? string.Empty : live;
    }

    /// <summary>The box gained focus: refreshes stop landing here.</summary>
    public void BeginEdit() => _isEditing = true;

    /// <summary>
    /// A drag across this cell's handle has started: refreshes stop landing
    /// here until <see cref="EndScrub"/>.
    /// </summary>
    // A drag writes faster than the engine publishes. Without the guard stale
    // refreshes make the number jitter backwards.
    public void BeginScrub() => _isScrubbing = true;

    /// <summary>Shows a value written by a drag, without committing anything.</summary>
    public void SetScrubText(string text)
    {
        _live = text;
        Text = text;
    }

    /// <summary>The drag ended: refreshes resume, unless somebody is typing.</summary>
    public void EndScrub() => _isScrubbing = false;

    /// <summary>
    /// Enter, or focus lost. Applies the value and hands the cell back to the
    /// refresh.
    /// </summary>
    public void Commit()
    {
        if (!_isEditing)
            return;

        _isEditing = false;
        string typed = Text.Trim();

        // Empty means "leave it alone" unless the cell allows an empty value.
        if (typed.Length == 0 && !AllowsEmpty)
        {
            Revert();
            return;
        }

        if (string.Equals(typed, _live, StringComparison.Ordinal) && !_isMixed)
        {
            Rejection = string.Empty;
            return;
        }

        // Clear before the commit, so a handler's own refusal survives.
        Rejection = string.Empty;
        _commit(this, typed);
    }

    /// <summary>Escape, or an unusable value: puts the live value back.</summary>
    public void Revert()
    {
        _isEditing = false;
        Text = _isMixed ? string.Empty : _live;
        Rejection = string.Empty;
    }

    /// <summary>Parses a number the way the panel writes one: invariant culture, finite only.</summary>
    public static bool TryParseNumber(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && float.IsFinite(value);

    /// <summary>Formats a number the way the panel reads one back.</summary>
    public static string Format(float value) =>
        MathF.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
