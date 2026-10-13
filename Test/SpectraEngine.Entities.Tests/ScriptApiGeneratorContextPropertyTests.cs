namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A property and a signal of a class of nodes that take the context in
/// front of the node. A static class has no fields, and the context is where
/// such a member finds what the level keeps for its scripts.
/// </summary>
public sealed class ScriptApiGeneratorContextPropertyTests
{
    private const string Signal = """
        /// <summary>Something a script connects a function to.</summary>
        [ScriptClass("RBXScriptSignal", TypeParameters = "T... = ...any")]
        internal sealed partial class Signal : ScriptObject
        {
            /// <summary>Calls the function at every fire.</summary>
            [ScriptMethod(Parameters = "callback: (T...) -> ()")]
            private int Connect(LuauStack stack) => 0;
        }

        """;

    [Fact]
    public void A_signal_and_a_property_of_a_node_that_take_the_context_are_handed_it_where_they_are_read()
    {
        GeneratorRun run = Run(Signal + """
            /// <summary>A part.</summary>
            [ScriptClass("Part")]
            internal static partial class PartApi
            {
                /// <summary>Fires when the player touches it.</summary>
                [ScriptSignal("character: Instance")]
                public static Signal Touched(ScriptContext context, SceneNode node) => new();

                /// <summary>How many touch it.</summary>
                [ScriptProperty]
                public static float Touching(ScriptContext context, SceneNode node) => 0f;

                /// <summary>Its name.</summary>
                [ScriptProperty]
                public static string Name(SceneNode node) => node.Name;
            }
            """);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();

        string part = Flat(run.SourceNamed("PartApi.ScriptApi.g.cs"));
        part.ShouldContain(
            "ScriptAtoms.Touched:returncontext.PushObject(stack,global::TestGame.Scripting.PartApi.Touched(context,self))?1:");
        part.ShouldContain("stack.Push((double)(global::TestGame.Scripting.PartApi.Touching(context,self)))");
        part.ShouldContain("stack.Push(global::TestGame.Scripting.PartApi.Name(self))");
        part.ShouldContain("Name=\"Touched\"");
    }

    [Theory]
    [InlineData("public static float Mass(ScriptContext context) => 1f;")]
    [InlineData("public static float Mass(SceneNode node, ScriptContext context) => 1f;")]
    [InlineData("public static float Mass(ScriptContext context, SceneNode node, int more) => 1f;")]
    public void A_property_with_the_context_and_no_node_after_it_is_refused(string member)
    {
        GeneratorRun run = Run($$"""
            /// <summary>A part.</summary>
            [ScriptClass("Part")]
            internal static partial class PartApi
            {
                /// <summary>Its name.</summary>
                [ScriptProperty]
                public static string Name(SceneNode node) => node.Name;

                /// <summary>How heavy it is.</summary>
                [ScriptProperty]
                {{member}}
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS005"]);
        run.CompileErrors().ShouldBeEmpty();
    }

    // The generator writes the body of a settable property, and that body
    // takes the node alone.
    [Fact]
    public void A_property_a_script_can_set_cannot_take_the_context()
    {
        GeneratorRun run = Run("""
            /// <summary>A node.</summary>
            [ScriptClass("Instance")]
            internal static partial class NodeApi
            {
                /// <summary>Its name.</summary>
                [ScriptProperty(Sets = SettableMemberId.Name)]
                public static partial string Name(ScriptContext context, SceneNode node);
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS007"]);
        run.Diagnostics.Single().GetMessage().ShouldContain("cannot take the context");
    }

    private static GeneratorRun Run(string classes) => ScriptApiHarness.Run(ScriptApiFixtures.Usings + classes);

    // Without the layout, so the lines of one statement can be read as one.
    private static string Flat(string source) => string.Concat(source.Where(c => !char.IsWhiteSpace(c)));
}
