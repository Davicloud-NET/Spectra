using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// A CFrame on its way in and out of Luau. The userdata holds the value
// itself, so one costs a script no managed object.
internal static class FrameValue
{
    // False: it could not be pushed, and an error value is on top instead.
    public static bool Push(LuauStack stack, CFrame value) => stack.PushUserdata(ScriptTags.CFrame, in value);

    public static bool TryGet(LuauStack stack, int index, out CFrame value) =>
        stack.TryGetUserdata(index, ScriptTags.CFrame, out value);
}
