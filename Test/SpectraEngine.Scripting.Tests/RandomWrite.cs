using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// One write of a random run: the member, the value as the door is handed it,
// and the Luau that hands a script's write a value of the same sort. One
// that puts back writes what the member read when the run began, and has
// no value of its own.
internal readonly record struct RandomWrite(
    SettableMember Member, MemberValue Value, string Luau, bool PutsBack = false);
