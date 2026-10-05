using SpectraEngine.Core.Entities;
using System;

namespace SpectraEngine.Entities;

/// <summary>
/// Holds a number a level can add to, subtract from and read back, with optional
/// bounds that announce themselves when the count reaches them.
/// </summary>
// OnHitMax and OnHitMin fire when the count arrives at a bound, not on every
// change that lands there. Whether it was already at the bound is read off the
// previous value, so SetValueNoFire needs no special case.
// Bounds apply only while max is above min. Setting a bound re-clamps and fires nothing.
[SpectraEntity("math_counter", Group = "Logic", Placement = EntityPlacement.Abstract)]
public sealed partial class MathCounter : Entity
{
    /// <summary>Fired with the new value whenever the count changes or is asked for.</summary>
    [EntityOutput]
    public const string OutValue = nameof(OutValue);

    /// <summary>Fired when the count arrives at its ceiling, once per arrival.</summary>
    [EntityOutput]
    public const string OnHitMax = nameof(OnHitMax);

    /// <summary>Fired when the count arrives at its floor, once per arrival.</summary>
    [EntityOutput]
    public const string OnHitMin = nameof(OnHitMin);

    /// <summary>The count this counter starts at, before any clamping.</summary>
    [Keyvalue(
        "startvalue",
        Display = "Start value",
        Tooltip = "The count at load, clamped into the bounds if there are any.",
        Default = "0")]
    public float StartValue { get; set; }

    /// <summary>The floor, active only while <see cref="Maximum"/> is above it.</summary>
    [Keyvalue(
        "min",
        Display = "Minimum",
        Tooltip = "The floor. Equal to the maximum means the counter is unclamped.",
        Default = "0")]
    public float Minimum { get; set; }

    /// <summary>The ceiling, active only while it is above <see cref="Minimum"/>.</summary>
    [Keyvalue(
        "max",
        Display = "Maximum",
        Tooltip = "The ceiling. Equal to the minimum means the counter is unclamped.",
        Default = "0")]
    public float Maximum { get; set; }

    /// <summary>The current count.</summary>
    public float Value { get; private set; }

    /// <summary>Whether the bounds are in force.</summary>
    public bool IsClamped => Maximum > Minimum;

    /// <summary>How many inputs carried an argument this counter could not use.</summary>
    public int RefusedInputCount { get; private set; }

    /// <inheritdoc/>
    protected override void OnSpawn() => Value = Clamp(StartValue);

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        state.Add("value", Value);
        state.Add("min", Minimum);
        state.Add("max", Maximum);
        state.Add("refused inputs", RefusedInputCount);
    }

    [EntityInput("Add")]
    private void Add(ref EntityInputContext context) => Move(Amount(ref context), ref context);

    [EntityInput("Subtract")]
    private void Subtract(ref EntityInputContext context) => Move(-Amount(ref context), ref context);

    [EntityInput("SetValue")]
    private void SetValue(ref EntityInputContext context)
    {
        if (TryRead(ref context, out float value))
            Assign(value, context.Activator, announce: true);
    }

    [EntityInput("SetValueNoFire")]
    private void SetValueNoFire(ref EntityInputContext context)
    {
        if (TryRead(ref context, out float value))
            Assign(value, context.Activator, announce: false);
    }

    [EntityInput("SetHitMax")]
    private void SetHitMax(ref EntityInputContext context)
    {
        if (!TryRead(ref context, out float value))
            return;

        Maximum = value;
        Value = Clamp(Value);
    }

    [EntityInput("SetHitMin")]
    private void SetHitMin(ref EntityInputContext context)
    {
        if (!TryRead(ref context, out float value))
            return;

        Minimum = value;
        Value = Clamp(Value);
    }

    [EntityInput("GetValue")]
    private void GetValue(ref EntityInputContext context) =>
        FireOutValue(context.Activator, KeyvalueWire.Format(Value));

    private void Move(float delta, ref EntityInputContext context) =>
        Assign(Value + delta, context.Activator, announce: true);

    private void Assign(float value, Entity? activator, bool announce)
    {
        // Overflow reaches infinity, which KeyvalueWire.Format cannot write.
        if (!float.IsFinite(value))
        {
            RefusedInputCount++;
            return;
        }

        bool wasAtMaximum = IsClamped && Value >= Maximum;
        bool wasAtMinimum = IsClamped && Value <= Minimum;

        Value = Clamp(value);

        if (!announce)
            return;

        FireOutValue(activator, KeyvalueWire.Format(Value));

        if (IsClamped && Value >= Maximum && !wasAtMaximum)
            FireOnHitMax(activator);

        if (IsClamped && Value <= Minimum && !wasAtMinimum)
            FireOnHitMin(activator);
    }

    private float Clamp(float value) => IsClamped ? Math.Clamp(value, Minimum, Maximum) : value;

    // An absent or unreadable argument means one.
    private float Amount(ref EntityInputContext context)
    {
        if (context.Parameter.Length == 0)
            return 1f;

        if (KeyvalueWire.TryParseFloat(context.Parameter, out float amount))
            return amount;

        RefusedInputCount++;
        return 1f;
    }

    // No default here: an unreadable argument leaves the count alone.
    private bool TryRead(ref EntityInputContext context, out float value)
    {
        if (KeyvalueWire.TryParseFloat(context.Parameter, out value))
            return true;

        RefusedInputCount++;
        return false;
    }
}
