namespace SpectraEngine.Scripting.Luau.Runtime;

// The userdata tags of the runtime. Luau has 128 for everything.
internal static class ScriptTags
{
    // A managed object a script holds that is no signal and no connection:
    // a node, a service, the player.
    public const int Object = 1;

    // A CFrame, held in the userdata itself.
    public const int CFrame = 2;

    // A Color3, held in the userdata itself.
    public const int Color3 = 3;

    // A signal and a connection are objects with a tag each. typeof reads
    // its answer off the metatable, and a tag has one metatable.
    public const int Signal = 4;

    public const int Connection = 5;

    // The tag an object is pushed under, which is the same on every push.
    public static int Of(object value) => value switch
    {
        ScriptSignal => Signal,
        ScriptConnection => Connection,
        _ => Object,
    };

    // Reads the object at the index, whichever of the three tags it has.
    public static LuauObjectLookup TryGet(LuauObjects objects, LuauStack stack, int index, out object? value)
    {
        LuauObjectLookup found = objects.TryGet(stack, index, Object, out value);
        if (found == LuauObjectLookup.NotThisKind)
            found = objects.TryGet(stack, index, Signal, out value);
        if (found == LuauObjectLookup.NotThisKind)
            found = objects.TryGet(stack, index, Connection, out value);

        return found;
    }
}
