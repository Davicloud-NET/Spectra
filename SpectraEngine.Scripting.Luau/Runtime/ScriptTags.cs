namespace SpectraEngine.Scripting.Luau.Runtime;

// The userdata tags of the runtime. Luau has 128 for everything.
internal static class ScriptTags
{
    // Every managed object a script holds: a node, a service, a signal, a connection.
    public const int Object = 1;

    // A CFrame, held in the userdata itself.
    public const int CFrame = 2;
}
