namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A class that declares no member of its own, and a member written by hand
/// that takes the context. Each gets what the binding needs to call it.
/// </summary>
public sealed class ScriptApiGeneratorHandOnTests
{
    private const string Self =
        "(global::SpectraEngine.Scripting.Luau.Runtime.ScriptContextcontext," +
        "global::SpectraEngine.Scripting.Luau.LuauStackstack,global::SpectraEngine.Core.Scene.SceneNodeself,intatom)";

    private const string Node = """
        /// <summary>A node.</summary>
        [ScriptClass("Instance")]
        internal static partial class NodeApi
        {
            /// <summary>Its name.</summary>
            [ScriptProperty]
            public static string Name(SceneNode node) => node.Name;
        }

        """;

    // Its own file has only what it is. The catalogue knows the type of its
    // object, so that is where its switches are.
    [Fact]
    public void A_class_that_declares_no_member_gets_switches_that_hand_every_name_to_the_class_it_extends()
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Representative);
        string catalogue = Flat(run.SourceNamed("ScriptApiCatalog.g.cs"));

        run.SourceNamed("WedgeApi.ScriptApi.g.cs").ShouldNotContain("ScriptRead");
        catalogue.ShouldContain("namespaceTestGame.Scripting{partialclassWedgeApi{");
        foreach (string access in new[] { "Read", "Write", "Invoke" })
        {
            catalogue.ShouldContain(
                $"internalstaticintScript{access}{Self}=>" +
                $"global::TestGame.Scripting.PartApi.Script{access}(context,stack,self,atom);");
        }
    }

    [Fact]
    public void A_line_of_classes_that_declare_nothing_hands_on_one_to_the_next()
    {
        GeneratorRun run = Run(Node + """
            /// <summary>A light.</summary>
            [ScriptClass("Light", Extends = "Instance")]
            internal static partial class LightApi;

            /// <summary>A light with a cone.</summary>
            [ScriptClass("SpotLight", Extends = "Light")]
            internal static partial class SpotLightApi;
            """);

        string catalogue = Flat(run.SourceNamed("ScriptApiCatalog.g.cs"));

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();
        catalogue.ShouldContain(
            $"partialclassLightApi{{//Lightdeclaresnomemberitself,soeachofthesehandstheatomtoInstance.internalstaticintScriptRead{Self}=>" +
            "global::TestGame.Scripting.NodeApi.ScriptRead(context,stack,self,atom);");
        catalogue.ShouldContain(
            $"partialclassSpotLightApi{{//SpotLightdeclaresnomemberitself,soeachofthesehandstheatomtoLight.internalstaticintScriptRead{Self}=>" +
            "global::TestGame.Scripting.LightApi.ScriptRead(context,stack,self,atom);");
    }

    // The switches move to the class's own file, and are not written twice.
    [Fact]
    public void A_class_that_gets_its_first_member_writes_its_switches_itself()
    {
        GeneratorRun run = Run(Node + """
            /// <summary>A light.</summary>
            [ScriptClass("Light", Extends = "Instance")]
            internal static partial class LightApi
            {
                /// <summary>How bright it is.</summary>
                [ScriptProperty]
                public static float Brightness(SceneNode node) => 1f;
            }

            /// <summary>A light with a cone.</summary>
            [ScriptClass("SpotLight", Extends = "Light")]
            internal static partial class SpotLightApi;
            """);

        string catalogue = Flat(run.SourceNamed("ScriptApiCatalog.g.cs"));

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();
        catalogue.ShouldNotContain("partialclassLightApi{");
        catalogue.ShouldContain("partialclassSpotLightApi{");
        Flat(run.SourceNamed("Scripting_LightApi.ScriptApi.g.cs")).ShouldContain(
            "default:returnglobal::SpectraEngine.Scripting.Luau.Runtime.ScriptBases.Read_TestGame_Scripting_LightApi(context,stack,self,atom);");
    }

    // A class of its own objects answers through the object. A static class
    // above it has no object to hand on.
    [Theory]
    [InlineData("Lamp")]
    [InlineData("Counter")]
    public void A_class_that_declares_no_member_and_extends_no_class_of_nodes_gets_no_switches(string extends)
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

            /// <summary>A counter with no member either.</summary>
            [ScriptClass("Counter")]
            internal static partial class CounterApi;

            /// <summary>A door.</summary>
            [ScriptClass("Door", Extends = "{{extends}}")]
            internal static partial class DoorApi;
            """);

        run.CompileErrors().ShouldBeEmpty();
        run.SourceNamed("ScriptApiCatalog.g.cs").ShouldNotContain("partial class DoorApi");
    }

    [Fact]
    public void A_method_written_by_hand_that_takes_the_context_is_handed_it_wherever_it_is_called()
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Representative);
        string node = Flat(run.SourceNamed("NodeApi.ScriptApi.g.cs"));

        node.ShouldContain(
            "ScriptAtoms.GetChildren:returnglobal::TestGame.Scripting.NodeApi.GetChildren(context,stack,self);");
        node.ShouldContain(
            ".Function(\"GetChildren\"u8,stack=>context.TryGetObject(stack,1,outglobal::SpectraEngine.Core.Scene.SceneNode?self)" +
            "?global::TestGame.Scripting.NodeApi.GetChildren(context,stack,self):");
    }

    [Fact]
    public void A_function_and_a_method_of_an_object_may_take_the_context_too()
    {
        GeneratorRun run = Run("""
            /// <summary>A lamp.</summary>
            [ScriptClass("Lamp")]
            internal sealed partial class Lamp : ScriptObject
            {
                /// <summary>Dims it.</summary>
                [ScriptMethod(Parameters = "seconds: number?")]
                private int Dim(ScriptContext context, LuauStack stack) => 0;
            }

            /// <summary>Counts.</summary>
            [ScriptLibrary("counter")]
            internal sealed partial class CounterLibrary
            {
                /// <summary>Counts up.</summary>
                [ScriptFunction(Name = "up")]
                private int Up(ScriptContext context, LuauStack stack) => 0;

                /// <summary>Counts down.</summary>
                [ScriptFunction(Name = "down")]
                private static int Down(ScriptContext context, LuauStack stack) => 0;
            }
            """);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();
        Flat(run.SourceNamed("Lamp.ScriptApi.g.cs")).ShouldContain("ScriptAtoms.Dim:returnself.Dim(context,stack);");

        string library = Flat(run.SourceNamed("CounterLibrary.ScriptApi.g.cs"));
        library.ShouldContain(".Function(\"up\"u8,stack=>this.Up(context,stack))");
        library.ShouldContain(".Function(\"down\"u8,stack=>global::TestGame.Scripting.CounterLibrary.Down(context,stack))");
    }

    [Theory]
    [InlineData("private static int Count(ScriptContext context, LuauStack stack) => 0;", "takes a LuauStack and then the object")]
    [InlineData("private static int Count(ScriptContext context, LuauStack stack, SceneNode node, int more) => 0;", "takes a LuauStack and then the object")]
    [InlineData("private static void Count(ScriptContext context, LuauStack stack, SceneNode node) { }", "returns an int")]
    public void A_method_with_the_context_and_another_shape_than_its_place_asks_for_is_refused(string member, string why)
    {
        GeneratorRun run = Run(Node + $$"""
            /// <summary>A part.</summary>
            [ScriptClass("Part", Extends = "Instance")]
            internal static partial class PartApi
            {
                /// <summary>How heavy it is.</summary>
                [ScriptProperty]
                public static float Mass(SceneNode node) => 1f;

                /// <summary>Counts.</summary>
                [ScriptMethod]
                {{member}}
            }
            """);

        run.DiagnosticIds.ShouldBe(["SPS009"]);
        run.Diagnostics.Single().GetMessage().ShouldContain(why);
        run.CompileErrors().ShouldBeEmpty();
    }

    private static GeneratorRun Run(string classes) => ScriptApiHarness.Run(ScriptApiFixtures.Usings + classes);

    // Without the layout, so the lines of one statement can be read as one.
    private static string Flat(string source) => string.Concat(source.Where(c => !char.IsWhiteSpace(c)));
}
