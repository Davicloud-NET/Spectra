using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities;

/// <summary>
/// Holds a true or false value a level can set, flip and ask about.
/// </summary>
// Setting and asking are separate. SetValue and Toggle store and fire nothing,
// so wiring can prime a branch without running it. Test fires, and
// SetValueTest and ToggleTest do both.
// The outputs carry no value: a wire on them sends its own parameter.
[SpectraEntity("logic_branch", Group = "Logic", Placement = EntityPlacement.Abstract)]
public sealed partial class LogicBranch : Entity
{
    /// <summary>Fired by a test while the value is true.</summary>
    [EntityOutput]
    public const string OnTrue = nameof(OnTrue);

    /// <summary>Fired by a test while the value is false.</summary>
    [EntityOutput]
    public const string OnFalse = nameof(OnFalse);

    /// <summary>The value this branch starts with.</summary>
    [Keyvalue(
        "initialvalue",
        Display = "Initial value",
        Tooltip = "The value the branch holds at load.",
        Default = "0")]
    public bool InitialValue { get; set; }

    /// <summary>The value the branch holds now.</summary>
    public bool Value { get; private set; }

    /// <summary>How many inputs carried an argument this branch could not use.</summary>
    public int RefusedInputCount { get; private set; }

    /// <inheritdoc/>
    protected override void OnSpawn() => Value = InitialValue;

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        state.Headline("value", Value ? "true" : "false");
        state.Add("value", Value);
        state.Add("refused inputs", RefusedInputCount);
    }

    [EntityInput("SetValue")]
    private void SetValue(ref EntityInputContext context) => TryStore(ref context);

    [EntityInput("SetValueTest")]
    private void SetValueTest(ref EntityInputContext context)
    {
        if (TryStore(ref context))
            Announce(context.Activator);
    }

    [EntityInput("Toggle")]
    private void Toggle(ref EntityInputContext context) => Value = !Value;

    [EntityInput("ToggleTest")]
    private void ToggleTest(ref EntityInputContext context)
    {
        Value = !Value;
        Announce(context.Activator);
    }

    [EntityInput("Test")]
    private void Test(ref EntityInputContext context) => Announce(context.Activator);

    private void Announce(Entity? activator)
    {
        if (Value)
            FireOnTrue(activator);
        else
            FireOnFalse(activator);
    }

    // "0" or "1" only. Anything else leaves the value alone, and a
    // SetValueTest carrying it tests nothing.
    private bool TryStore(ref EntityInputContext context)
    {
        if (KeyvalueWire.TryParseBool(context.Parameter, out bool value))
        {
            Value = value;
            return true;
        }

        RefusedInputCount++;
        return false;
    }
}
