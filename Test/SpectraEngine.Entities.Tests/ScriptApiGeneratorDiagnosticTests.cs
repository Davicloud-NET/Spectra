using Microsoft.CodeAnalysis;

namespace SpectraEngine.Entities.Tests;

// Each source is wrong in one way. A bad declaration is an error at the
// declaration, and never a generated file that does not compile.
public sealed class ScriptApiGeneratorDiagnosticTests
{
    private string _source = "";

    [Fact]
    public void A_class_that_is_not_partial_is_refused_and_nothing_is_emitted_for_it()
    {
        GeneratorRun run = Run("""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static class CounterApi
            {
                /// <summary>Its length.</summary>
                [ScriptProperty]
                public static float Length(Vector3 self) => self.Length();
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS001"]);
        run.SourceCount.ShouldBe(0);
    }

    [Fact]
    public void A_member_with_no_summary_is_refused_and_names_the_member()
    {
        GeneratorRun run = Run("""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                // An ordinary comment is no summary.
                [ScriptProperty]
                public static float Length(Vector3 self) => self.Length();
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS002"]);
        run.Diagnostics.Single().GetMessage().ShouldContain("'Length' has no /// summary");
        LineOf(run.Diagnostics.Single()).ShouldContain("Length(Vector3 self)");
    }

    [Fact]
    public void A_class_with_no_summary_is_refused()
    {
        GeneratorRun run = Run("""
            [ScriptLibrary("counter")]
            internal static partial class CounterLibrary
            {
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS002"]);
        run.Diagnostics.Single().GetMessage().ShouldContain("'CounterLibrary'");
    }

    [Theory]
    [InlineData("public static Guid Id(Vector3 self) => default;", "System.Guid")]
    [InlineData("public static float[] Parts(Vector3 self) => [];", "float[]")]
    public void A_property_of_a_type_no_script_value_is_made_from_is_refused(string member, string type)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Something.</summary>
                [ScriptProperty]
                {{member}}
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS003"]);
        run.Diagnostics.Single().GetMessage().ShouldContain($"the type '{type}'");
        run.Diagnostics.Single().GetMessage().ShouldContain("take a LuauStack");
    }

    [Theory]
    [InlineData("public static float Scale(Vector3 self, decimal by) => 0f;")]
    [InlineData("public static float Scale(Vector3 self, ref float by) => 0f;")]
    [InlineData("public static object Scale(Vector3 self) => self;")]
    [InlineData("public static ReadOnlySpan<byte> Scale(Vector3 self) => default;")]
    public void A_method_with_a_parameter_or_a_result_that_cannot_cross_is_refused(string member)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Something.</summary>
                [ScriptMethod]
                {{member}}
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS003"]);
        run.CompileErrors().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("goal Vector3", "has no name and colon in front of its type")]
    [InlineData("goal: Vector3, alpha:", "'alpha' has no type")]
    [InlineData("2nd: number", "'2nd' is no name for a parameter")]
    [InlineData("callback: (number -> ()", "a bracket is never closed")]
    public void Parameters_that_do_not_read_as_Luau_writes_them_are_refused_at_the_attribute(string text, string why)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A counter.</summary>
            [ScriptLibrary("counter")]
            internal static partial class CounterLibrary
            {
                /// <summary>Counts.</summary>
                [ScriptFunction(Parameters = "{{text}}")]
                private static int Count(LuauStack stack) => 0;
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS004"]);
        run.Diagnostics.Single().GetMessage().ShouldContain(why);
        LineOf(run.Diagnostics.Single()).ShouldContain("[ScriptFunction(Parameters");
    }

    // A word Luau keeps would end the definitions file at that line.
    [Theory]
    [InlineData("[ScriptMethod(Name = \"end\")] public static float Stop(Vector3 self) => 0f;", "'end' on 'Stop'")]
    [InlineData("[ScriptMethod] public static float Mix(Vector3 self, Vector3 @then) => 0f;", "'then' on 'Mix'")]
    [InlineData("[ScriptMethod] public static float Mix(Vector3 self, float alpha, bool @nil = false) => 0f;", "'nil' on 'Mix'")]
    [InlineData("[ScriptMethod(Parameters = \"until: number\")] public static int Wait(LuauStack stack, Vector3 self) => 0;", "'until' is no name for a parameter")]
    public void A_word_Luau_keeps_for_itself_is_refused_as_a_name(string member, string named)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Something.</summary>
                {{member}}
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS004"]);
        run.Diagnostics.Single().GetMessage().ShouldContain(named);
        run.Diagnostics.Single().GetMessage().ShouldContain("a word Luau keeps for itself");
        run.CompileErrors().ShouldBeEmpty();
        run.SourceNamed("CounterApi.ScriptApi.g.cs").ShouldNotContain("Signatures");
    }

    [Theory]
    [InlineData("local", "a word Luau keeps for itself")]
    [InlineData("Zähler", "letters from a to z")]
    public void A_class_under_what_is_no_name_in_Luau_is_refused(string name, string why)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A counter.</summary>
            [ScriptValue("{{name}}")]
            internal static partial class CounterApi
            {
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS004"]);
        run.Diagnostics.Single().GetMessage().ShouldContain(why);
        LineOf(run.Diagnostics.Single()).ShouldContain("[ScriptValue(");
    }

    [Fact]
    public void A_parameter_Luau_cannot_name_is_refused_at_the_parameter()
    {
        GeneratorRun run = Run("""
            /// <summary>A counter.</summary>
            [ScriptLibrary("counter")]
            internal static partial class CounterLibrary
            {
                /// <summary>The part between two numbers.</summary>
                [ScriptFunction(Name = "between")]
                public static double Between(double start, double @end) => @end - start;
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS004"]);
        Diagnostic refused = run.Diagnostics.Single();
        _source.Substring(refused.Location.SourceSpan.Start, refused.Location.SourceSpan.Length).ShouldBe("@end");
    }

    [Fact]
    public void An_operator_Luau_does_not_have_is_refused_at_the_attribute()
    {
        GeneratorRun run = Run("""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            [ScriptOperator("**", "Counter", "Counter")]
            internal static partial class CounterApi
            {
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS004"]);
        LineOf(run.Diagnostics.Single()).ShouldContain("[ScriptOperator(\"**\"");
    }

    [Theory]
    [InlineData("public static float Length() => 0f;")]
    [InlineData("public static float Length(float scale) => 0f;")]
    [InlineData("public static float Length(CFrame self) => 0f;")]
    public void A_member_of_a_static_class_that_does_not_take_the_object_first_is_refused(string member)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Adds to it.</summary>
                [ScriptMethod]
                public static Vector3 Add(Vector3 self, float amount) => self;

                /// <summary>Its length.</summary>
                [ScriptMethod]
                {{member}}
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS005"]);
        run.Diagnostics.Single().GetMessage().ShouldContain("'Length' must be static and take the object");
        run.CompileErrors().ShouldBeEmpty();
    }

    [Fact]
    public void Two_members_of_one_class_under_one_name_are_refused()
    {
        GeneratorRun run = Run("""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Its length.</summary>
                [ScriptProperty]
                public static float Length(Vector3 self) => self.Length();

                /// <summary>Its length again.</summary>
                [ScriptMethod(Name = "Length")]
                public static float GetLength(Vector3 self) => self.Length();
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS006"]);
        run.Diagnostics.Single().GetMessage().ShouldContain("'Length' is declared twice in 'Counter'");
        run.CompileErrors().ShouldBeEmpty();
    }

    [Fact]
    public void Two_classes_under_one_name_are_refused_once_on_the_second()
    {
        GeneratorRun run = Run("""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
            }

            /// <summary>Another counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class OtherCounterApi
            {
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS006"]);
        LineOf(run.Diagnostics.Single()).ShouldContain("OtherCounterApi");
        run.CompileErrors().ShouldBeEmpty();
    }

    // The description gives a class the members of the one it extends. Where
    // the binding cannot hand them on, a script would be promised too much.
    [Theory]
    [InlineData("Missing", "'Missing' is no class declared with [ScriptClass]")]
    [InlineData("Counter", "'Counter' is no class declared with [ScriptClass]")]
    [InlineData("Lamp", "the members of 'Lamp' are of another object")]
    [InlineData("Door", "go round in a circle")]
    public void A_class_that_extends_what_the_binding_cannot_follow_is_refused(string extends, string why)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A lamp.</summary>
            [ScriptClass("Lamp")]
            internal sealed partial class Lamp : ScriptObject
            {
                /// <summary>Whether it shines.</summary>
                [ScriptProperty]
                public bool Enabled { get; set; }
            }

            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Its length.</summary>
                [ScriptProperty]
                public static float Length(Vector3 self) => self.Length();
            }

            /// <summary>A door.</summary>
            [ScriptClass("Door", Extends = "{{extends}}")]
            internal static partial class DoorApi
            {
                /// <summary>How far it is open.</summary>
                [ScriptProperty]
                public static float Opening(SceneNode node) => 0f;
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS010"]);
        run.Diagnostics.Single().GetMessage().ShouldContain($"'Door' extends '{extends}'");
        run.Diagnostics.Single().GetMessage().ShouldContain(why);
        LineOf(run.Diagnostics.Single()).ShouldContain("DoorApi");
        run.CompileErrors().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("public static partial bool Name(SceneNode node);", "the door takes a string, a Vector3 or a CFrame")]
    [InlineData("public static string Name(SceneNode node) => node.Name;", "declare it as a partial method with no body")]
    public void A_settable_property_the_door_cannot_serve_is_refused(string member, string why)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A node.</summary>
            [ScriptClass("Instance")]
            internal static partial class NodeApi
            {
                /// <summary>Its name.</summary>
                [ScriptProperty(Sets = SettableMemberId.Name)]
                {{member}}
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS007"]);
        run.Diagnostics.Single().GetMessage().ShouldContain(why);
        LineOf(run.Diagnostics.Single()).ShouldContain("[ScriptProperty(Sets");
    }

    [Fact]
    public void A_settable_property_of_what_is_no_node_is_refused()
    {
        GeneratorRun run = Run("""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Where it is.</summary>
                [ScriptProperty(Sets = SettableMemberId.Position)]
                public static partial Vector3 Position(Vector3 self);
            }
            """);

        run.DiagnosticIds.ShouldContain("SPS007");
        run.Diagnostics.First(d => d.Id == "SPS007").GetMessage().ShouldContain("the door only sets a member of a node");
    }

    [Theory]
    [InlineData("[ScriptFunction]", "Declare it with [ScriptMethod]")]
    [InlineData("[ScriptMethod, ScriptProperty]", "a member is one of a property, a signal, a method and a function")]
    [InlineData("[ScriptSignal]", "a signal is a member of a class")]
    public void An_attribute_on_a_member_it_does_not_belong_on_is_refused(string attribute, string why)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Its length.</summary>
                {{attribute}}
                public static float Length(Vector3 self) => self.Length();
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS008"]);
        run.Diagnostics.Single().GetMessage().ShouldContain(why);
        run.CompileErrors().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("[ScriptValue(\"Counter\")] internal sealed partial class Counter", "a value type is declared on a static class")]
    [InlineData("[ScriptClass(\"Counter\")] internal sealed partial class Counter", "must derive from ScriptObject")]
    [InlineData("[ScriptClass(\"Counter\"), ScriptLibrary(\"Counter\")] internal static partial class Counter", "a class declares one of")]
    public void An_attribute_on_a_class_it_does_not_belong_on_is_refused_and_nothing_is_emitted(string declaration, string why)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A counter.</summary>
            {{declaration}}
            {
            }
            """);

        run.DiagnosticIds.Distinct().ShouldBe(["SPS008"]);
        run.Diagnostics[0].GetMessage().ShouldContain(why);
        run.SourceCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("[ScriptFunction] private static void Count(LuauStack stack) { }", "it takes a LuauStack and nothing else, and returns an int")]
    [InlineData("[ScriptFunction] private static int Count(LuauStack stack, int more) => 0;", "it takes a LuauStack and nothing else")]
    [InlineData("[ScriptFunction(Returns = \"number\")] public static double Count(double a) => a;", "its parameters are its own")]
    [InlineData("[ScriptFunction] public static T Count<T>(T a) => a;", "an ordinary method that is not generic")]
    public void A_function_of_a_shape_that_cannot_be_called_is_refused(string member, string why)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A counter.</summary>
            [ScriptLibrary("counter")]
            internal static partial class CounterLibrary
            {
                /// <summary>Counts.</summary>
                {{member}}
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS009"]);
        run.Diagnostics.Single().GetMessage().ShouldContain(why);
        run.CompileErrors().ShouldBeEmpty();
    }

    [Fact]
    public void A_refused_member_does_not_stop_the_rest_of_the_class_being_emitted()
    {
        GeneratorRun run = Run("""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Its length.</summary>
                [ScriptProperty]
                public static float Length(Vector3 self) => self.Length();

                /// <summary>Its id.</summary>
                [ScriptProperty]
                public static Guid Id(Vector3 self) => default;
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS003"]);
        run.CompileErrors().ShouldBeEmpty();
        string counter = run.SourceNamed("CounterApi.ScriptApi.g.cs");
        counter.ShouldContain("ScriptAtoms.Length:");
        counter.ShouldNotContain("ScriptAtoms.Id:");
    }

    private GeneratorRun Run(string classes)
    {
        _source = ScriptApiFixtures.Usings + classes;
        return ScriptApiHarness.Run(_source);
    }

    // The line of the source a diagnostic points at.
    private string LineOf(Diagnostic diagnostic)
    {
        diagnostic.Location.Kind.ShouldNotBe(LocationKind.None, "the diagnostic points at no source");
        return _source.ReplaceLineEndings("\n").Split('\n')[diagnostic.Location.GetLineSpan().StartLinePosition.Line];
    }
}
