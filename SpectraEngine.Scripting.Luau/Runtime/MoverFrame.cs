using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The tick a script's mover runs in, which is what the four objects it is
// handed read and write. The state is a copy: the player gets it only when
// the mover has returned. Between ticks the frame is closed, and the
// objects then read the player as it is and write nothing.
internal sealed class MoverFrame
{
    public const string OnlyWhileMoving = "This can be done only by the mover, while it runs.";

    private readonly PlayerWrites _door;
    private readonly EntityWorld _world;
    private CharacterState _state;
    private CharacterState _start;
    private CharacterState _resting;
    private CharacterButtons _previous;

    public MoverFrame(PlayerWrites door, EntityWorld world)
    {
        _door = door;
        _world = world;
    }

    public bool IsOpen { get; private set; }

    // What the player asked for on the tick the mover ran last.
    public CharacterCommand Command { get; private set; }

    public Vector3 MoveDirection { get; private set; }

    // As the mover's own tick gets them. Null until a mover has run.
    public ICharacterCollisionSource? Source { get; private set; }

    public CharacterTuning? Tuning { get; private set; }

    // Where the player was when the tick the mover runs in began.
    public Vector3 StartedAt => _start.Position;

    public void Open(in CharacterState state, in CharacterCommand command, ICharacterCollisionSource source, CharacterTuning tuning)
    {
        _state = state;
        _start = state;
        _previous = state.PrevButtons;

        // As the built-in mover starts a tick.
        _state.PrevButtons = command.Buttons;
        _state.SteppedUpBy = 0f;
        _state.SlideContacts = default;

        Command = command;
        MoveDirection = command.MoveDirection;
        Source = source;
        Tuning = tuning;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    // What the mover has made of the state. Read once it has returned.
    public ref readonly CharacterState Result => ref _state;

    // False when the player was moved past the mover while it ran, as by a
    // teleport the mover's own script made. That move stands, and what the
    // mover made of its copy is dropped.
    public bool IsWhereItStarted(in CharacterState now) =>
        now.Position == _start.Position && now.Velocity == _start.Velocity && now.Grounded == _start.Grounded;

    public ref readonly CharacterState Read()
    {
        if (IsOpen)
            return ref _state;

        _resting = (_door.Player ?? throw new InvalidOperationException(ScriptMember.HasLeft)).State;
        return ref _resting;
    }

    public ref CharacterState Write()
    {
        if (!IsOpen)
            throw new InvalidOperationException(OnlyWhileMoving);

        return ref _state;
    }

    public CharacterTuning TuningNow() =>
        Tuning ?? throw new InvalidOperationException("The tuning is read once a mover has run.");

    public bool IsDown(CharacterButtons button) => (Command.Buttons & button) != 0;

    // Held now and not on the tick before. The command carries no edge:
    // one command is ticked several times in a frame.
    public bool IsPressed(CharacterButtons button) => IsDown(button) && (_previous & button) == 0;

    public SceneNode? NodeOf(Guid id) =>
        id != Guid.Empty && _world.Scene.TryFindById(id, out SceneNode? node) ? node : null;
}
