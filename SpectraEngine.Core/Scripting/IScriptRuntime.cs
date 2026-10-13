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

    /// <summary>A queued wake will not come: the tick it was made in made too many of them.</summary>
    /// <returns>
    /// True when what waited for the wake was ended. False when it still
    /// waits, because the runtime has asked for another wake.
    /// </returns>
    bool Drop(long token);

    /// <summary>
    /// What a node fired has come due for one listener the runtime added to
    /// <see cref="EntityWorld.Listeners"/>. A listener removed since then
    /// still gets what was queued for it, and the runtime drops that.
    /// </summary>
    /// <param name="listener">What the runtime named the listener by when it added it.</param>
    /// <param name="parameter">What was sent with the fire, as a wire carries it. Empty for nothing.</param>
    void Hear(long listener, string parameter);

    /// <summary>
    /// A touch of a part has started or ended, and has come due for one
    /// listener the runtime added to <see cref="EntityWorld.Listeners"/>
    /// under a name of <see cref="TouchSignal"/>.
    /// </summary>
    /// <param name="listener">What the runtime named the listener by when it added it.</param>
    /// <param name="visitor">Who touches. Its player is null when the player has left the level since.</param>
    void HearTouch(long listener, in TouchVisitor visitor);

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
