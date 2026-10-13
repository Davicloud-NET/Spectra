using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// A mover written in Luau: every tick it calls a script's function with the
// state, the command, the collision, the tuning and the step. The function
// works on a copy of the state. When it fails, waits or is stopped, the
// built-in mover moves the player on that same tick, from the state as it was.
internal sealed class LuauCharacterMover : ICharacterMover
{
    public const string TookOver = "The built-in mover took over.";

    // For a mover that was no longer the level's when it failed.
    public const string MovedThisTick = "The built-in mover moved the player on this tick.";

    private readonly LuauHost _host;
    private readonly MoverCall _call;
    private readonly MoverFrame _frame;
    private readonly MoverCollision _collision;
    private readonly LuauReference[] _handed;
    private readonly Func<LuauCharacterMover, bool> _failed;
    private bool _isOut;

    // `handed` is what the function is called with before the step, in its
    // order. `failed` is told once, when this mover fails, and says whether
    // the built-in mover took its place.
    public LuauCharacterMover(
        LuauHost host,
        MoverCall call,
        MoverFrame frame,
        MoverCollision collision,
        LuauReference[] handed,
        Func<LuauCharacterMover, bool> failed)
    {
        _host = host;
        _call = call;
        _frame = frame;
        _collision = collision;
        _handed = handed;
        _failed = failed;
    }

    // The script whose function this calls.
    public LuauScript? Script => _call.Script;

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

    // For a mover another took the place of: lets its function and its
    // thread go.
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
            _frame.Open(in state, in command, source, tuning);
            _collision.Begin(in state, tuning, deltaTime);
            if (_call.TryRun(_handed, deltaTime, out failure))
            {
                if (_frame.IsWhereItStarted(in state))
                    state = _frame.Result;

                return true;
            }

            LetGo();
        }
        catch (Exception e) // boundary: nothing the tick calls may throw
        {
            _isOut = true;
            failure = new ScriptMessage(
                ScriptSeverity.Error, $"The mover failed: {e.Message}", _call.Script?.Name ?? string.Empty, 0);
        }
        finally
        {
            _frame.Close();
        }

        // Nothing to say when the level stopped under the call.
        if (failure is { } said)
            Say(in said);

        return false;
    }

    // The slot first: the built-in mover is in the mover's place before the
    // line is told, whatever the sink then does.
    private void Say(in ScriptMessage failure)
    {
        string after = _failed(this) ? TookOver : MovedThisTick;
        if (_host.IsClosed)
            return;

        string end = failure.Text.Length > 0 && failure.Text[^1] is '.' or '!' or '?' ? " " : ". ";
        _host.Report(failure with { Text = failure.Text + end + after });
    }
}
