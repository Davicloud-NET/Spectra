using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// CFrame in a script against <see cref="CFrame"/> in the engine. The
/// userdata holds the engine's value, so the two must agree to the bit.
/// </summary>
public sealed class CFrameScriptTests
{
    private static Vector3 A => ScriptValues.A;

    private static Vector3 B => ScriptValues.B;

    private static CFrame F => ScriptValues.F;

    private static CFrame G => ScriptValues.G;

    [Fact]
    public void Every_constructor_builds_the_frame_the_engine_builds()
    {
        (string Luau, CFrame Core)[] cases =
        [
            ("CFrame.new()", CFrame.Identity),
            ("CFrame.identity", CFrame.Identity),
            ("CFrame.new(a)", new CFrame(A)),
            ("CFrame.new(1.5, -2, 3)", new CFrame(1.5f, -2f, 3f)),
            ("CFrame.new(a, b)", CFrame.LookAt(A, B)),
            ("CFrame.new(1, 2, 3, 0, 0.6, 0, 0.8)", new CFrame(A, new Quaternion(0f, 0.6f, 0f, 0.8f))),
            ("CFrame.new(1, 2, 3, 0, 0, 1, 0, 1, 0, -1, 0, 0)",
                CFrame.FromMatrix(A, new Vector3(0f, 0f, -1f), Vector3.UnitY, Vector3.UnitX)),
            ("CFrame.Angles(0.1, 0.2, 0.3)", CFrame.Angles(0.1f, 0.2f, 0.3f)),
            ("CFrame.fromEulerAnglesXYZ(0.1, 0.2, 0.3)", CFrame.FromEulerAnglesXYZ(0.1f, 0.2f, 0.3f)),
            ("CFrame.fromEulerAnglesYXZ(0.1, 0.2, 0.3)", CFrame.FromEulerAnglesYXZ(0.1f, 0.2f, 0.3f)),
            ("CFrame.fromOrientation(0.1, 0.2, 0.3)", CFrame.FromOrientation(0.1f, 0.2f, 0.3f)),
            ("CFrame.lookAt(a, b)", CFrame.LookAt(A, B)),
            ("CFrame.lookAt(a, b, Vector3.zAxis)", CFrame.LookAt(A, B, Vector3.UnitZ)),
            ("CFrame.lookAlong(a, b)", CFrame.LookAlong(A, B)),
            ("CFrame.lookAlong(a, b, Vector3.xAxis)", CFrame.LookAlong(A, B, Vector3.UnitX)),
            ("CFrame.fromAxisAngle(a, 0.7)", CFrame.FromAxisAngle(A, 0.7f)),
            ("CFrame.fromMatrix(a, Vector3.zAxis, Vector3.yAxis)", CFrame.FromMatrix(A, Vector3.UnitZ, Vector3.UnitY)),
            ("CFrame.fromMatrix(a, Vector3.zAxis, Vector3.yAxis, -Vector3.xAxis)",
                CFrame.FromMatrix(A, Vector3.UnitZ, Vector3.UnitY, -Vector3.UnitX)),
        ];

        float[][] built = ScriptValues.Frames([.. cases.Select(one => one.Luau)]);

        for (int i = 0; i < cases.Length; i++)
            built[i].ShouldBe(ScriptValues.Components(cases[i].Core), cases[i].Luau);
    }

    [Fact]
    public void The_operators_and_the_methods_that_give_a_frame_are_the_engines()
    {
        (string Luau, CFrame Core)[] cases =
        [
            ("f * g", F * G),
            ("f + a", F + A),
            ("f - a", F - A),
            ("f:Inverse()", F.Inverse()),
            ("f:Lerp(g, 0.25)", F.Lerp(G, 0.25f)),
            ("f:ToWorldSpace(g)", F.ToWorldSpace(G)),
            ("f:ToObjectSpace(g)", F.ToObjectSpace(G)),
            ("f:Orthonormalize()", F.Orthonormalize()),
            ("f.Rotation", new CFrame(Vector3.Zero, F.Rotation)),
            ("f * CFrame.identity", F),
        ];

        float[][] built = ScriptValues.Frames([.. cases.Select(one => one.Luau)]);

        for (int i = 0; i < cases.Length; i++)
            built[i].ShouldBe(ScriptValues.Components(cases[i].Core), cases[i].Luau);
    }

    [Fact]
    public void The_properties_and_the_methods_that_give_a_vector_are_the_engines()
    {
        (string Luau, Vector3 Core)[] cases =
        [
            ("f * a", F * A),
            ("f.Position", F.Position),
            ("f.LookVector", F.LookVector),
            ("f.RightVector", F.RightVector),
            ("f.UpVector", F.UpVector),
            ("f.XVector", F.XVector),
            ("f.YVector", F.YVector),
            ("f.ZVector", F.ZVector),
            ("f:PointToWorldSpace(a)", F.PointToWorldSpace(A)),
            ("f:PointToObjectSpace(a)", F.PointToObjectSpace(A)),
            ("f:VectorToWorldSpace(a)", F.VectorToWorldSpace(A)),
            ("f:VectorToObjectSpace(a)", F.VectorToObjectSpace(A)),
            ("Vector3.new(f:ToEulerAnglesXYZ())", F.ToEulerAnglesXYZ()),
            ("Vector3.new(f:ToEulerAnglesYXZ())", F.ToEulerAnglesYXZ()),
            ("Vector3.new(f:ToOrientation())", F.ToOrientation()),
            ("Vector3.new(f.X, f.Y, f.Z)", F.Position),
        ];

        Vector3[] read = ScriptValues.Vectors([.. cases.Select(one => one.Luau)]);

        for (int i = 0; i < cases.Length; i++)
            read[i].ShouldBe(cases[i].Core, cases[i].Luau);
    }

    [Fact]
    public void A_frame_is_a_value_with_the_type_name_CFrame()
    {
        string[] printed = ScriptLevel.Run("""
            local f = CFrame.new(1, 2, 3)
            print(typeof(f), f == CFrame.new(1, 2, 3), f == CFrame.new(1, 2, 4), f == 5)
            print(f)
            print(select('#', f:components()), f:FuzzyEq(f + Vector3.new(0, 0.000001, 0)), f:FuzzyEq(f + Vector3.one))
            print(pcall(function() return f.Size end))
            print(pcall(function() f.Position = Vector3.zero end))
            print(pcall(function() return f * 5 end))
            print(pcall(function() return f:Lerp(5, 1) end))
            print(pcall(function() return CFrame.new(1, 2) end))
            """);

        printed[0].ShouldBe("CFrame true false false");
        printed[1].ShouldBe("1, 2, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1");
        printed[2].ShouldBe("12 true false");
        printed[3].ShouldEndWith("Size is not a valid member of CFrame");
        printed[4].ShouldEndWith("Position cannot be assigned to");
        printed[5].ShouldEndWith("attempt to perform arithmetic (mul) on CFrame and number");
        printed[6].ShouldEndWith("invalid argument #1 to 'Lerp' (CFrame expected, got number)");
        printed[7].ShouldEndWith("CFrame.new takes nothing, a position, a position and a point to look at, or 3, 7 or 12 numbers");
    }

    [Fact]
    public void A_node_reads_and_takes_frames_and_positions_in_world_space_and_in_its_parents()
    {
        using var level = new ScriptLevel();
        SceneNode arm = level.Part("Arm", new Vector3(1f, 2f, 3f));
        arm.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        SceneNode hand = level.Part("Hand", new Vector3(0f, 1f, 0f), arm);
        level.AddScript(hand, """
            local hand = script.Parent
            print(hand.Position == hand.CFrame.Position, hand.LocalPosition == hand.LocalCFrame.Position)
            hand.Position += Vector3.new(0, 1, 0)
            hand.LocalCFrame *= CFrame.Angles(0, 0.25, 0)
            hand.Parent.CFrame = CFrame.new(5, 0, 0)
            hand.LocalPosition = Vector3.new(0, 0, 2)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true true"]);
        arm.CFrame.ShouldBe(new CFrame(5f, 0f, 0f));
        hand.LocalPosition.ShouldBe(new Vector3(0f, 0f, 2f));
        hand.LocalCFrame.FuzzyEq(new CFrame(0f, 0f, 2f) * CFrame.Angles(0f, 0.25f, 0f)).ShouldBeTrue();
        hand.WorldPosition.ShouldBe(new Vector3(5f, 0f, 2f));

        level.Stop();

        arm.LocalPosition.ShouldBe(new Vector3(1f, 2f, 3f));
        arm.LocalRotation.ShouldBe(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f));
        hand.LocalPosition.ShouldBe(new Vector3(0f, 1f, 0f));
        hand.LocalRotation.ShouldBe(Quaternion.Identity);
    }
}
