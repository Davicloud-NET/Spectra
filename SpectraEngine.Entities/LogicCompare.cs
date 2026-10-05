using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities;

/// <summary>
/// Holds a number and a number to compare it against, and reports how the two stand.
/// </summary>
// Equal means the same float, with no tolerance. Whole numbers are exact up to
// 16777216, which covers a counter stepping by ones. A sum of fractions rarely
// lands on its target, so test one with OnLessThan or OnGreaterThan.
// SetValue and SetCompareValue only store. Compare and SetValueCompare fire.
// The outputs carry no value: a wire on them sends its own parameter.
[SpectraEntity("logic_compare", Group = "Logic", Placement = EntityPlacement.Abstract)]
public sealed partial class LogicCompare : Entity
{
    /// <summary>Fired by a comparison while the value is below the compare value.</summary>
    [EntityOutput]
    public const string OnLessThan = nameof(OnLessThan);

    /// <summary>Fired by a comparison while the two values are the same.</summary>
    [EntityOutput]
    public const string OnEqualTo = nameof(OnEqualTo);

    /// <summary>Fired by a comparison while the two values differ, before the direction.</summary>
    [EntityOutput]
    public const string OnNotEqualTo = nameof(OnNotEqualTo);

    /// <summary>Fired by a comparison while the value is above the compare value.</summary>
    [EntityOutput]
    public const string OnGreaterThan = nameof(OnGreaterThan);

    /// <summary>The value this entity starts with.</summary>
    [Keyvalue(
        "initialvalue",
        Display = "Initial value",
        Tooltip = "The value held at load.",
        Default = "0")]
    public float InitialValue { get; set; }

    /// <summary>The number the value is compared against.</summary>
    [Keyvalue(
        "comparevalue",
        Display = "Compare value",
        Tooltip = "The number the value is compared against.",
        Default = "0")]
    public float CompareValue { get; set; }

    /// <summary>The value held now.</summary>
    public float Value { get; private set; }

    /// <summary>How many inputs carried an argument this entity could not use.</summary>
    public int RefusedInputCount { get; private set; }

    /// <inheritdoc/>
    protected override void OnSpawn() => Value = InitialValue;

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        state.Headline("value", HeadlineText.Number(Value));
        state.Add("value", Value);
        state.Add("compare value", CompareValue);
        state.Add("refused inputs", RefusedInputCount);
    }

    [EntityInput("SetValue")]
    private void SetValue(ref EntityInputContext context)
    {
        if (TryRead(ref context, out float value))
            Value = value;
    }

    [EntityInput("SetValueCompare")]
    private void SetValueCompare(ref EntityInputContext context)
    {
        if (!TryRead(ref context, out float value))
            return;

        Value = value;
        Announce(context.Activator);
    }

    [EntityInput("SetCompareValue")]
    private void SetCompareValue(ref EntityInputContext context)
    {
        if (TryRead(ref context, out float value))
            CompareValue = value;
    }

    [EntityInput("Compare")]
    private void Compare(ref EntityInputContext context) => Announce(context.Activator);

    private void Announce(Entity? activator)
    {
        if (Value == CompareValue)
        {
            FireOnEqualTo(activator);
            return;
        }

        FireOnNotEqualTo(activator);

        if (Value < CompareValue)
            FireOnLessThan(activator);
        else
            FireOnGreaterThan(activator);
    }

    // An unreadable argument stores nothing, and a SetValueCompare carrying it
    // compares nothing.
    private bool TryRead(ref EntityInputContext context, out float value)
    {
        if (KeyvalueWire.TryParseFloat(context.Parameter, out value))
            return true;

        RefusedInputCount++;
        return false;
    }
}
