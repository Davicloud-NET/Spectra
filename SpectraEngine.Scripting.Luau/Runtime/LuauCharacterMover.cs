using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>
/// A mover written in Luau: it calls a script's function on every tick
/// with the state, the command, the collision, the tuning and the step,
/// and the player gets the state the function leaves. When the function
/// fails, waits or is stopped, the built-in mover moves the player on that
/// same tick and on every tick after.
/// </summary>
// The function works on a copy of the state. A tick it did not finish
// leaves the player's own state untouched for the built-in mover.
internal sealed class LuauCharacterMover : ICharacterMover
{
    public const string TookOver = "The built-in mover took over.";

    private readonly LuauHost _host;
    private readonly MoverCall _call;
    private readonly MoverFrame _frame;
    private readonly MoverCollision _collision;
    private readonly LuauReference[] _handed;
    private readonly Action _failed;
    private bool _isOut;

    /// <param name="handed">What the function is called with before the step, in its order.</param>
    /// <param name="failed">Told once, when the built-in mover takes over.</param>
    public LuauCharacterMover(
        LuauHost host, MoverCall call, MoverFrame frame, MoverCollision collision, LuauReference[] handed, Action failed)
    {
        _host = host;
        _call = call;
        _frame = frame;
        _collision = collision;
        _handed = handed;
        _failed = failed;
    }

    /// <summary>The script whose function this calls. Null when no script handed it over.</summary>
    public LuauScript? Script => _call.Script;

    /// <inheritdoc/>
    public void Tick(
        ref CharacterState state,
        in CharacterCommand command,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        float deltaTime)
    {
        if (!_isOut && TryMove(ref state, in command, source, tuning, deltaTime))
            return;

        DefaultCharacterMover.Instance.Tick(ref state, in command, source, tuning, deltaTime);
    }

    /// <summary>Lets the function and its thread go, for a mover another took the place of.</summary>
    public void LetGo()
    {
        _isOut = true;
        _call.LetGo();
    }

    private bool TryMove(
        ref CharacterState state,
        in CharacterCommand command,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        float deltaTime)
    {
        ScriptMessage? failure;
        try
        {
            _frame.Open(in state, in command, source, tuning, deltaTime);
            _collision.Begin(in state, tuning, deltaTime);
            if (_call.TryRun(_handed, deltaTime, out failure))
            {
                state = _frame.Result;
                return true;
            }
        }
        catch (Exception e) // boundary: nothing the tick calls may throw
        {
            failure = new ScriptMessage(
                ScriptSeverity.Error, $"The mover failed: {e.Message}", _call.Script?.Name ?? string.Empty, 0);
        }
        finally
        {
            _frame.Close();
        }

        _isOut = true;
        _call.LetGo();

        // Nothing to say when the level stopped under the call.
        if (failure is { } said)
        {
            _failed();
            Say(in said);
        }

        return false;
    }

    private void Say(in ScriptMessage failure)
    {
        if (_host.IsClosed)
            return;

        string end = failure.Text.Length > 0 && failure.Text[^1] is '.' or '!' or '?' ? " " : ". ";
        _host.Report(failure with { Text = failure.Text + end + TookOver });
    }
}
