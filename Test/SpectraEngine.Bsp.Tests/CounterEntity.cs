using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

// Adds what it is sent, fires OutValue with the count and OnHitMax when the
// count reaches its ceiling. It describes its state, so ent_show has some.
internal sealed class CounterEntity : Entity
{
    public const string OutValue = nameof(OutValue);
    public const string OnHitMax = nameof(OnHitMax);
    public const string OnHitMin = nameof(OnHitMin);

    private float _value;
    private float _max;
    private int _refusedInputs;

    public static EntitySchema Schema { get; } = new(
        "counter",
        keyvalues: [Keyvalue("startvalue"), Keyvalue("max")],
        inputs: ["Add", "SetValue"],
        outputs: [OutValue, OnHitMax, OnHitMin]);

    public override bool ParseKeyValue(string key, string value)
    {
        switch (key)
        {
            case "startvalue":
                KeyvalueWire.TryParseFloat(value, out _value);
                return true;
            case "max":
                KeyvalueWire.TryParseFloat(value, out _max);
                return true;
            default:
                return false;
        }
    }

    public override bool AcceptInput(string input, ref EntityInputContext context)
    {
        if (input is not ("Add" or "SetValue"))
            return false;

        if (!KeyvalueWire.TryParseFloat(context.Parameter, out float amount))
        {
            _refusedInputs++;
            return true;
        }

        _value = input == "Add" ? _value + amount : amount;
        FireOutput(OutValue, context.Activator, KeyvalueWire.Format(_value));
        if (_max > 0f && _value >= _max)
            FireOutput(OnHitMax, context.Activator);

        return true;
    }

    public override void DescribeState(EntityStateWriter state)
    {
        state.Add("value", _value);
        state.Add("refused inputs", _refusedInputs);
    }

    private static KeyvalueDescriptor Keyvalue(string name) =>
        new(name, "", "", "0", KeyvalueType.Float, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
            KeyvalueDescriptor.NoChoices);
}
