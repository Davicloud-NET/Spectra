using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// One thread that waits in WaitForChild.
internal readonly record struct ChildWait(
    // The thread's wait among all waits, which is where the thread is kept.
    long Token,

    SceneNode Parent,

    // The name the child must have.
    string Name,

    // The tick the wait gives up on and answers nil. Never for a wait with no timeout.
    long Deadline,

    // The tick a wait with no timeout says it is still waiting. Never once it has.
    long WarnAt,

    // That warning, placed where the script called. Its words are written when it is said.
    ScriptMessage Warning);
