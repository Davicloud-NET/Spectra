using SpectraEngine.Core.Entities;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// What runs a level's scripts while it plays. One is made for each play
/// session and closed when the level stops. The entity world and the play
/// session call it at fixed points of a tick, and none of its methods throws.
/// </summary>
public interface IScriptRuntime
{
    /// <summary>
    /// Loads and starts the level's scripts, in scene tree order. Called
    /// once, at the head of the first tick.
    /// </summary>
    void StartScripts();

    /// <summary>A wake queued with <see cref="EntityWorld.QueueScriptWake"/> has come due.</summary>
    void Wake(long token);

    /// <summary>A queued wake will not come: the tick it was made in made too many events.</summary>
    void Drop(long token);

    /// <summary>The point of a tick before the physics step.</summary>
    void BeforeSimulation();

    /// <summary>The end of a tick, also of one in which nothing walks.</summary>
    void AfterSimulation();

    /// <summary>
    /// Ends every script. Any thread may call it, also while a call into a
    /// script has not returned. It waits for nothing.
    /// </summary>
    void Close();
}
