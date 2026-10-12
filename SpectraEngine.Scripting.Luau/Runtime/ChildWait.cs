using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// One thread that waits in WaitForChild.
internal readonly record struct ChildWait(
    LuauThread Thread,

    // The thread's wait among all waits.
    long Token,

    SceneNode Parent,

    // The name the child must have.
    string Name,

    // The tick the wait gives up on and answers nil. Never for a wait with no timeout.
    long Deadline,

    // The tick a wait with no timeout says it is still waiting. Never once it has.
    long WarnAt,

    // Where the script called, for the warning.
    string Script,

    int Line);
