using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Scripting.Tests;

// A walk over the demo's course that asks a mover for everything it does:
// through the start room's door, up a staircase, through a doorway, off the
// terrace, a sprint, up a ramp, off its platform, into the side of a part,
// a jump onto it and off again, and a slide along a wall into a corner.
internal static class MoverCourse
{
    // The yaw is (cos, 0, sin), so 0 walks +X and a quarter turn walks +Z.
    private const float East = 0f;
    private const float South = MathF.PI / 2f;
    private const float West = MathF.PI;
    private const float North = -MathF.PI / 2f;
    private const float SouthEast = MathF.PI / 4f;

    public static readonly (CharacterCommand Command, int Ticks)[] Legs =
    [
        (default, 20),
        (Walk(East), 335),
        (Walk(North, CharacterButtons.Sprint), 140),
        (Walk(East), 200),
        (Walk(South), 187),
        (Walk(West), 40),
        (Walk(West, CharacterButtons.Jump), 1),
        (Walk(West), 100),
        (Walk(SouthEast), 300),
        (Walk(West, CharacterButtons.Sprint), 120),
        (default, 30),
    ];

    public static int TickCount => Legs.Sum(leg => leg.Ticks);

    public static CharacterCommand Walk(float yaw, CharacterButtons buttons = CharacterButtons.None) =>
        new() { MoveForward = CharacterCommand.Axis(1f), Yaw = yaw, Buttons = buttons };

    // Plays the legs on a level that has started. The state after every tick.
    public static List<CharacterState> Run(MoverLevel level)
    {
        var states = new List<CharacterState>(TickCount);
        foreach ((CharacterCommand command, int ticks) in Legs)
        {
            for (int i = 0; i < ticks; i++)
            {
                level.Tick(in command);
                states.Add(level.State);
            }
        }

        return states;
    }
}
