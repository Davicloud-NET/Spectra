using SpectraEngine.Core.Entities;

namespace SpectraEngine.Scripting.Tests;

// An entity that does nothing and declares a keyvalue of every type a
// script reads as a value of its own: what GetAttribute is asked about.
internal sealed class SignEntity : Entity
{
    public const string Class = "sign";

    public static EntitySchema Schema { get; } = new(Class, keyvalues:
    [
        Declared("lit", KeyvalueType.Bool, "1"),
        Declared("count", KeyvalueType.Int, "3"),
        Declared("speed", KeyvalueType.Float, "1.5"),
        Declared("reach", KeyvalueType.Distance, "8"),
        Declared("label", KeyvalueType.String, "Sign"),
        Declared("mode", KeyvalueType.Choices, "steady"),
        Declared("offset", KeyvalueType.Vec3, "0 0 0"),
        Declared("facing", KeyvalueType.Angles, "0 90 0"),
        Declared("tint", KeyvalueType.Color, "1 1 1"),
        Declared("size", KeyvalueType.Vec2, "1 2"),
        Declared("partner", KeyvalueType.NodeRef, ""),
    ]);

    // Every keyvalue is taken and none is used.
    public override bool ParseKeyValue(string key, string value) => true;

    private static KeyvalueDescriptor Declared(string name, KeyvalueType type, string value) =>
        new(name, "", "", value, type, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u, KeyvalueDescriptor.NoChoices);
}
