namespace SpectraEngine.Entities.Tests;

// A property of an object that is no node, which a script sets through a
// method of the object.
public sealed class ScriptApiGeneratorSetByTests
{
    private const string Arguments = "global::SpectraEngine.Scripting.Luau.Runtime.ScriptArguments";
    private const string Atoms = "global::SpectraEngine.Scripting.Luau.Runtime.ScriptAtoms";

    private const string Dial = ScriptApiFixtures.Usings + """
        /// <summary>A dial a script turns.</summary>
        [ScriptClass("Dial")]
        internal sealed partial class Dial : ScriptObject
        {
            /// <summary>How far it is turned.</summary>
            [ScriptProperty(SetBy = nameof(SetLevel))]
            public float Level { get; private set; }

            /// <summary>Where it points.</summary>
            [ScriptProperty(SetBy = nameof(SetAim))]
            public Vector3 Aim { get; private set; }

            /// <summary>Whether it is lit.</summary>
            [ScriptProperty(Name = "Lit", SetBy = nameof(SetLit))]
            public bool IsLit { get; private set; }

            /// <summary>What is written on it.</summary>
            [ScriptProperty(SetBy = nameof(SetLabel))]
            public string Label { get; private set; } = "";

            /// <summary>How it is turned.</summary>
            [ScriptProperty(SetBy = nameof(SetFrame))]
            public CFrame Frame { get; private set; }

            /// <summary>How many times it was turned.</summary>
            [ScriptProperty]
            public int Turns { get; private set; }

            private string? SetLevel(float value)
            {
                if (value < 0f)
                    return "Level cannot be below zero";

                Level = value;
                Turns++;
                return null;
            }

            private string? SetAim(Vector3 value) { Aim = value; return null; }

            private string? SetLit(bool value) { IsLit = value; return null; }

            private string? SetLabel(string value) { Label = value; return null; }

            private string? SetFrame(CFrame value) { Frame = value; return null; }
        }
        """;

    [Fact]
    public void A_class_with_properties_set_by_its_own_methods_is_bound_and_compiles()
    {
        GeneratorRun run = ScriptApiHarness.Run(Dial);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();
    }

    [Fact]
    public void A_write_reads_the_value_and_hands_it_to_the_method_whose_answer_is_the_scripts_error()
    {
        string dial = Flat(ScriptApiHarness.Run(Dial).SourceNamed("Dial.ScriptApi.g.cs"));

        dial.ShouldContain(
            $"case{Atoms}.Level:{{if(!{Arguments}.TryDouble(stack,3,outdoublevalue))" +
            $"return{Arguments}.WrongValue(stack,\"Level\",\"number\");" +
            "returnself.SetLevel((float)value)is{}refusal?stack.Fail(refusal):0;}");
    }

    [Theory]
    [InlineData("Aim", "TryVector(stack,3,outglobal::System.Numerics.Vector3value)", "Vector3", "SetAim(value)")]
    [InlineData("Lit", "TryBoolean(stack,3,outboolvalue)", "boolean", "SetLit(value)")]
    [InlineData("Label", "TryText(stack,3,outstring?value)", "string", "SetLabel(value)")]
    [InlineData("Frame", "TryFrame(stack,3,outglobal::SpectraEngine.Core.Scene.CFramevalue)", "CFrame", "SetFrame(value)")]
    public void A_value_of_each_kind_is_read_by_its_own_reader_under_the_name_a_script_calls_the_property(
        string name, string reader, string luauType, string call)
    {
        string dial = Flat(ScriptApiHarness.Run(Dial).SourceNamed("Dial.ScriptApi.g.cs"));

        dial.ShouldContain(
            $"case{Atoms}.{name}:{{if(!{Arguments}.{reader})" +
            $"return{Arguments}.WrongValue(stack,\"{name}\",\"{luauType}\");" +
            $"returnself.{call}is{{}}refusal?stack.Fail(refusal):0;}}");
    }

    [Fact]
    public void A_property_with_no_method_stays_read_only()
    {
        string dial = Flat(ScriptApiHarness.Run(Dial).SourceNamed("Dial.ScriptApi.g.cs"));

        dial.ShouldContain(
            $"case{Atoms}.Turns:returnglobal::SpectraEngine.Scripting.Luau.Runtime.ScriptMember.ReadOnly;");
    }

    [Fact]
    public void The_description_says_a_script_can_set_it_and_names_no_member_of_the_door()
    {
        string dial = ScriptApiHarness.Run(Dial).SourceNamed("Dial.ScriptApi.g.cs");

        dial.ShouldContain(
            "Name = \"Level\", Type = \"number\", Summary = \"How far it is turned.\", Sets = null, CanSet = true, ");
        dial.ShouldContain("Name = \"Lit\", Type = \"boolean\", Summary = \"Whether it is lit.\", Sets = null, CanSet = true, ");
        dial.ShouldContain(
            "Name = \"Turns\", Type = \"number\", Summary = \"How many times it was turned.\", Sets = null, Deprecated");
    }

    [Theory]
    [InlineData("private string? SetLevel(double value) => null;", "takes a float")]
    [InlineData("private void SetLevel(float value) { }", "returns a string")]
    [InlineData("private static string? SetLevel(float value) => null;", "must be a method of the class")]
    [InlineData("private string? SetLevel(float value, bool loudly) => null;", "must be a method of the class")]
    [InlineData("private string? SetOther(float value) => null;", "SetBy names 'SetLevel'")]
    public void A_method_of_another_shape_is_refused_at_the_attribute(string method, string says)
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Usings + $$"""
            /// <summary>A dial a script turns.</summary>
            [ScriptClass("Dial")]
            internal sealed partial class Dial : ScriptObject
            {
                /// <summary>How far it is turned.</summary>
                [ScriptProperty(SetBy = "SetLevel")]
                public float Level { get; private set; }

                {{method}}
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS009"]);
        run.Diagnostics.Single().GetMessage().ShouldContain("'Level' cannot be bound: SetBy names 'SetLevel'");
        run.Diagnostics.Single().GetMessage().ShouldContain(says);
        run.CompileErrors().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("public Dial? Next { get; private set; }", "Dial? value")]
    [InlineData("public float? Level { get; private set; }", "float? value")]
    public void A_value_no_reader_takes_is_refused(string property, string parameter)
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Usings + $$"""
            /// <summary>A dial a script turns.</summary>
            [ScriptClass("Dial")]
            internal sealed partial class Dial : ScriptObject
            {
                /// <summary>Something of it.</summary>
                [ScriptProperty(SetBy = nameof(Set))]
                {{property}}

                private string? Set({{parameter}}) => null;
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS009"]);
        run.Diagnostics.Single().GetMessage().ShouldContain(
            "SetBy takes a number, a boolean, a string, a Vector3 or a CFrame");
    }

    [Fact]
    public void A_member_of_a_node_is_refused_and_told_to_go_through_the_door()
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Usings + """
            /// <summary>A node of the level.</summary>
            [ScriptClass("Instance")]
            internal static partial class NodeApi
            {
                /// <summary>How heavy it is.</summary>
                [ScriptProperty(SetBy = nameof(SetMass))]
                public static float Mass(SceneNode node) => 1f;

                private static string? SetMass(float value) => null;
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS009"]);
        run.Diagnostics.Single().GetMessage().ShouldContain(
            "one with [ScriptClass] that is not static. A member of a node is set through Sets");
    }

    [Fact]
    public void A_constant_of_a_library_is_refused()
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Usings + """
            /// <summary>Numbers.</summary>
            [ScriptLibrary("numbers")]
            internal sealed partial class Numbers
            {
                /// <summary>The biggest of them.</summary>
                [ScriptProperty(SetBy = nameof(SetMost))]
                public float Most { get; private set; }

                private string? SetMost(float value) => null;
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS009"]);
        run.Diagnostics.Single().GetMessage().ShouldContain("SetBy is for a class whose members are its own");
    }

    // Without the layout, so the lines of one statement can be read as one.
    private static string Flat(string source) => string.Concat(source.Where(c => !char.IsWhiteSpace(c)));
}
