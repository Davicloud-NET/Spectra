using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Scripting.Tests;

// What the tests of the player share: the first lines of a script that
// reaches the character, a command that walks, and the numbers a script
// printed read back as the floats they began as.
internal static class PlayerScripts
{
    public const string Character = """
        local Players = game:GetService("Players")
        local character = Players.LocalPlayer.Character
        """;

    // Straight ahead along the yaw, which is +X at zero.
    public static CharacterCommand Forward(float yaw = 0f) =>
        new() { MoveForward = CharacterCommand.Axis(1f), Yaw = yaw };

    public static CharacterSimulation Walker(ScriptLevel level) => level.Session.Character.ShouldNotBeNull();

    // Ticks with a command of the test's own, as a server or a replay would.
    public static void Tick(ScriptLevel level, in CharacterCommand command, int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            level.Session.Tick(ScriptLevel.Dt, in command);
    }

    // Luau prints a number as the shortest text that reads back the same
    // double, and every double here began as a float.
    public static float[] Numbers(string line) =>
        [.. line.Split(' ').Select(part => (float)double.Parse(part, CultureInfo.InvariantCulture))];

    public static Vector3 Vector(string line) => new(Numbers(line));
}
