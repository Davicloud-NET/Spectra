using System.Globalization;
using System.Numerics;
using System.Text;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

// Runs Luau expressions in a level and reads their values back as the floats
// the engine's own types hold, so a test compares the two bit for bit.
internal static class ScriptValues
{
    // What every expression may name.
    private const string Shared = """
        local a = Vector3.new(1, 2, 3)
        local b = Vector3.new(-4, 0.5, 2)
        local f = CFrame.new(1, 2, 3) * CFrame.Angles(0.1, 0.2, 0.3)
        local g = CFrame.lookAt(Vector3.new(4, 5, 6), Vector3.new(-1, 0, 2))
        """;

    public static Vector3 A => new(1f, 2f, 3f);

    public static Vector3 B => new(-4f, 0.5f, 2f);

    public static CFrame F => new CFrame(1f, 2f, 3f) * CFrame.Angles(0.1f, 0.2f, 0.3f);

    public static CFrame G => CFrame.LookAt(new Vector3(4f, 5f, 6f), new Vector3(-1f, 0f, 2f));

    // One line of numbers for each expression, which may give several values.
    public static float[][] Numbers(params string[] expressions) => Print(expressions, "print({0})");

    public static Vector3[] Vectors(params string[] expressions) =>
        [.. Print(expressions, "do local v = {0} print(v.X, v.Y, v.Z) end").Select(parts => new Vector3(parts))];

    // The twelve components of each frame, in the order of GetComponents.
    public static float[][] Frames(params string[] expressions) => Print(expressions, "print(({0}):GetComponents())");

    public static float[] Components(CFrame frame)
    {
        float[] components = new float[CFrame.ComponentCount];
        frame.GetComponents(components);
        return components;
    }

    private static float[][] Print(string[] expressions, string statement)
    {
        var script = new StringBuilder(Shared).AppendLine();
        foreach (string expression in expressions)
            script.AppendFormat(CultureInfo.InvariantCulture, statement, expression).AppendLine();

        string[] printed = ScriptLevel.Run(script.ToString());
        printed.Length.ShouldBe(expressions.Length);
        return [.. printed.Select(Parse)];
    }

    // Luau prints a number as the shortest text that reads back the same
    // double, and every double here began as a float.
    private static float[] Parse(string line) =>
        [.. line.Split(' ').Select(part => (float)double.Parse(part, CultureInfo.InvariantCulture))];
}
