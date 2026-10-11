using System.Numerics;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Vector3 in a script against <see cref="Vector3"/> in the engine. A script's
/// vector is Luau's own, three floats, so the two must agree to the bit.
/// </summary>
public sealed class Vector3ScriptTests
{
    private static Vector3 A => ScriptValues.A;

    private static Vector3 B => ScriptValues.B;

    [Fact]
    public void The_constructor_and_the_constants_are_the_engines_vectors()
    {
        Vector3[] vectors = ScriptValues.Vectors(
            "Vector3.new(0.1, -2.5, 300)",
            "Vector3.new()",
            "Vector3.new(7)",
            "Vector3.new(7, 8)",
            "(function(make) return make(1, 2, 3) end)(Vector3.new)",
            "Vector3.zero",
            "Vector3.one",
            "Vector3.xAxis",
            "Vector3.yAxis",
            "Vector3.zAxis");

        vectors.ShouldBe(
        [
            new Vector3(0.1f, -2.5f, 300f),
            Vector3.Zero,
            new Vector3(7f, 0f, 0f),
            new Vector3(7f, 8f, 0f),
            new Vector3(1f, 2f, 3f),
            Vector3.Zero,
            Vector3.One,
            Vector3.UnitX,
            Vector3.UnitY,
            Vector3.UnitZ,
        ]);
    }

    [Fact]
    public void The_arithmetic_is_the_engines()
    {
        Vector3[] vectors = ScriptValues.Vectors("a + b", "a - b", "a * b", "a / b", "a * 2.5", "2.5 * a", "a / 4", "-a");

        vectors.ShouldBe([A + B, A - B, A * B, A / B, A * 2.5f, 2.5f * A, A / 4f, -A]);
    }

    [Fact]
    public void The_members_Roblox_adds_are_the_engines()
    {
        float[][] numbers = ScriptValues.Numbers("a.Magnitude", "a:Dot(b)", "a.X", "a.Y", "a.Z", "a['z']");
        Vector3[] vectors = ScriptValues.Vectors("a.Unit", "a:Cross(b)", "a:Lerp(b, 0.25)", "a:Lerp(b, 1.5)");

        numbers.ShouldBe([[A.Length()], [Vector3.Dot(A, B)], [A.X], [A.Y], [A.Z], [A.Z]]);
        vectors.ShouldBe(
        [
            Vector3.Normalize(A),
            Vector3.Cross(A, B),
            Vector3.Lerp(A, B, 0.25f),
            Vector3.Lerp(A, B, 1.5f),
        ]);
    }

    [Fact]
    public void A_vector_is_a_value_with_the_type_name_Vector3()
    {
        string[] printed = ScriptLevel.Run("""
            local a = Vector3.new(1, 2, 3)
            print(typeof(a), a == Vector3.new(1, 2, 3), a == Vector3.new(1, 2, 4))
            print(a)
            print(pcall(function() return a.Size end))
            print(pcall(function() return a:Dot(5) end))
            """);

        printed[0].ShouldBe("Vector3 true false");
        printed[1].ShouldBe("1, 2, 3");
        printed[2].ShouldEndWith("Size is not a valid member of Vector3");
        printed[3].ShouldEndWith("invalid argument #1 to 'Dot' (Vector3 expected, got number)");
    }
}
