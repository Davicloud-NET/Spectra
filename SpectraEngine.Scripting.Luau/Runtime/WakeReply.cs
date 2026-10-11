namespace SpectraEngine.Scripting.Luau.Runtime;

// What a thread is handed when its wait is over.
internal enum WakeReply : byte
{
    // Nothing of the scheduler's: task.defer and task.delay pass on only
    // what the script gave them.
    Nothing,

    // The seconds that passed, which task.wait returns.
    Elapsed,

    // The seconds that passed and the level's time, as the old wait, spawn
    // and delay give them.
    ElapsedAndTime,
}
