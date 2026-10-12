using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The old spellings that still work, since so many Roblox scripts begin
// with them. Each is the task library's function of the same name, and
// hands back the two times the old one did.
[ScriptGlobals]
internal sealed partial class OldTaskGlobals(TaskLibrary tasks)
{
    /// <summary>Waits so many seconds, and at least until the next tick. Returns the seconds that passed and the level's time.</summary>
    [ScriptFunction(Name = "wait", Parameters = "seconds: number?", Returns = "(number, number)")]
    [ScriptDeprecated("task.wait")]
    private int Wait(LuauStack stack) => tasks.OldWait(stack);

    /// <summary>Runs a function on the next tick, with the seconds that passed and the level's time.</summary>
    [ScriptFunction(Name = "spawn", Parameters = "callback: (number, number) -> ...any")]
    [ScriptDeprecated("task.spawn")]
    private int Spawn(LuauStack stack) => tasks.OldSpawn(stack);

    /// <summary>Runs a function after so many seconds, with the seconds that passed and the level's time.</summary>
    [ScriptFunction(Name = "delay", Parameters = "seconds: number, callback: (number, number) -> ...any")]
    [ScriptDeprecated("task.delay")]
    private int Delay(LuauStack stack) => tasks.OldDelay(stack);
}
