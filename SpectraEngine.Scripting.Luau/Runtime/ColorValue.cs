using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// A Color3 on its way in and out of Luau. The userdata holds the value
// itself, so one costs a script no managed object.
internal static class ColorValue
{
    // False: it could not be pushed, and an error value is on top instead.
    public static bool Push(LuauStack stack, Color3 value) => stack.PushUserdata(ScriptTags.Color3, in value);

    public static bool TryGet(LuauStack stack, int index, out Color3 value) =>
        stack.TryGetUserdata(index, ScriptTags.Color3, out value);
}
