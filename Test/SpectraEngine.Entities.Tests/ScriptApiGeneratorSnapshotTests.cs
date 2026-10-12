using System.Threading.Tasks;

namespace SpectraEngine.Entities.Tests;

public sealed class ScriptApiGeneratorSnapshotTests
{
    [Fact]
    public Task Generated_source_for_a_representative_api_matches_snapshot()
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Representative);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        return Verify(run.AllSources());
    }

    [Fact]
    public void The_generated_source_compiles_against_what_the_binding_has()
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Representative);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();
    }

    [Fact]
    public void Every_class_gets_a_file_and_the_catalogue_one_more()
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Representative);

        run.SourceCount.ShouldBe(9);
    }

    // The description gives a class every member of the one it extends, so
    // its switches hand a name they do not have to that class.
    [Fact]
    public void A_class_that_extends_another_hands_it_every_name_it_does_not_declare()
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Representative);
        string part = Flat(run.SourceNamed("PartApi.ScriptApi.g.cs"));
        string catalogue = Flat(run.SourceNamed("ScriptApiCatalog.g.cs"));

        foreach (string access in new[] { "Read", "Write", "Invoke" })
        {
            part.ShouldContain(
                "default:returnglobal::SpectraEngine.Scripting.Luau.Runtime.ScriptBases." +
                $"{access}_TestGame_Scripting_PartApi(context,stack,self,atom);");
            catalogue.ShouldContain(
                $"{access}_TestGame_Scripting_PartApi(global::SpectraEngine.Scripting.Luau.Runtime.ScriptContextcontext," +
                "global::SpectraEngine.Scripting.Luau.LuauStackstack,global::SpectraEngine.Core.Scene.SceneNodeself,intatom)=>" +
                $"global::TestGame.Scripting.NodeApi.Script{access}(context,stack,self,atom);");
        }

        Flat(run.SourceNamed("NodeApi.ScriptApi.g.cs")).ShouldNotContain("ScriptBases");
    }

    // A call is a switch on a number: the catalogue numbers the names, and
    // the class switches on those numbers.
    [Fact]
    public void A_method_call_is_a_case_of_its_atom_in_a_switch()
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Representative);

        string catalogue = run.SourceNamed("ScriptApiCatalog.g.cs");
        catalogue.ShouldContain("public const int FindFirstChild = ");
        catalogue.ShouldContain("public const int AtomCount = 21;");

        string node = Flat(run.SourceNamed("NodeApi.ScriptApi.g.cs"));
        node.ShouldContain(
            "internalstaticintScriptInvoke(global::SpectraEngine.Scripting.Luau.Runtime.ScriptContextcontext," +
            "global::SpectraEngine.Scripting.Luau.LuauStackstack,global::SpectraEngine.Core.Scene.SceneNodeself,intatom)" +
            "{switch(atom){caseglobal::SpectraEngine.Scripting.Luau.Runtime.ScriptAtoms.FindFirstChild:" +
            "return__FindFirstChild(context,stack,self);");
    }

    [Fact]
    public void The_names_are_numbered_in_the_order_of_the_names_and_listed_in_that_order()
    {
        string catalogue = ScriptApiHarness.Run(ScriptApiFixtures.Representative).SourceNamed("ScriptApiCatalog.g.cs");

        catalogue.ShouldContain("public const int CFrame = 0;");
        catalogue.ShouldContain("public const int Connect = 1;");
        catalogue.ShouldContain("\"CFrame\\0Connect\\0Dim\\0Enabled\\0FindFirstChild\\0");
        catalogue.ShouldContain("public const int Instance = 0;");
        catalogue.ShouldContain("public const int Lamp = 1;");
    }

    [Fact]
    public void A_write_of_a_settable_property_goes_through_the_door_and_nothing_else_is_written()
    {
        string node = Flat(ScriptApiHarness.Run(ScriptApiFixtures.Representative).SourceNamed("NodeApi.ScriptApi.g.cs"));

        node.ShouldContain("returncontext.WriteText(stack,self,global::SpectraEngine.Core.Scripting.SettableMemberId.Name);");
        node.ShouldContain("returncontext.WriteVector(stack,self,global::SpectraEngine.Core.Scripting.SettableMemberId.Position);");
        node.ShouldContain("returncontext.WriteFrame(stack,self,global::SpectraEngine.Core.Scripting.SettableMemberId.CFrame);");
        node.ShouldContain("returnstack.Fail(\"Parentcannotbesetyet\");");
        node.ShouldContain("ScriptAtoms.GetChildren:returnglobal::SpectraEngine.Scripting.Luau.Runtime.ScriptMember.ReadOnly;");
    }

    // The read of a settable property is the door's own, so what a script
    // reads is what it wrote.
    [Fact]
    public void A_settable_property_is_read_from_the_door_with_the_accessibility_it_was_declared_with()
    {
        string node = Flat(ScriptApiHarness.Run(ScriptApiFixtures.Representative).SourceNamed("NodeApi.ScriptApi.g.cs"));

        node.ShouldContain(
            "internalstaticpartialglobal::System.Numerics.Vector3Position(global::SpectraEngine.Core.Scene.SceneNodenode)=>" +
            "global::SpectraEngine.Core.Scripting.ScriptWrites.Read(node,global::SpectraEngine.Core.Scripting.SettableMemberId.Position).Vector;");
        node.ShouldContain("publicstaticpartialstringName(");
    }

    [Fact]
    public void An_argument_is_checked_and_a_wrong_one_is_refused_in_the_words_a_script_reads()
    {
        string point = Flat(ScriptApiHarness.Run(ScriptApiFixtures.Representative).SourceNamed("PointApi.ScriptApi.g.cs"));

        point.ShouldContain(
            "if(!global::SpectraEngine.Scripting.Luau.Runtime.ScriptArguments.TryVector(stack,2,outglobal::System.Numerics.Vector3a0))" +
            "returnglobal::SpectraEngine.Scripting.Luau.Runtime.ScriptArguments.Wrong(stack,2,\"Lerp\",\"Vector3\",isMethod:true);");
    }

    [Fact]
    public void An_argument_with_a_default_may_be_left_out_and_then_has_it()
    {
        string point = Flat(ScriptApiHarness.Run(ScriptApiFixtures.Representative).SourceNamed("PointApi.ScriptApi.g.cs"));

        point.ShouldContain("doublea1=0.5f;if(!stack.IsNil(3)){");
        point.ShouldContain("global::System.Numerics.Vector3?a1=null;if(!stack.IsNil(3)){");
        point.ShouldContain("string?a2=null;if(!stack.IsNil(4)){");
        point.ShouldContain("PointApi.Lerp(self,a0,(float)a1)");
    }

    // A number the generator formats itself reads differently from one
    // host of the compiler to the next, and NaN reads as no number at all.
    [Theory]
    [InlineData("float by = 1e-5f", "doublea0=1e-5f;")]
    [InlineData("double by = -0.25", "doublea0=-0.25;")]
    [InlineData("float by = float.NaN", "doublea0=global::System.BitConverter.Int32BitsToSingle(-4194304);")]
    [InlineData("float by = float.PositiveInfinity", "doublea0=global::System.BitConverter.Int32BitsToSingle(2139095040);")]
    [InlineData("double by = double.NegativeInfinity", "doublea0=global::System.BitConverter.Int64BitsToDouble(-4503599627370496L);")]
    [InlineData("double by = Math.PI", "doublea0=global::System.BitConverter.Int64BitsToDouble(4614256656552045848L);")]
    public void A_default_is_written_as_its_author_wrote_it_or_by_its_bits(string parameter, string local)
    {
        GeneratorRun run = ScriptApiHarness.Run(ScriptApiFixtures.Usings + $$"""
            /// <summary>A counter.</summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Makes it longer.</summary>
                [ScriptMethod]
                public static Vector3 Scale(Vector3 self, {{parameter}}) => self * (float)by;
            }
            """);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();
        Flat(run.SourceNamed("CounterApi.ScriptApi.g.cs")).ShouldContain(local);
    }

    [Fact]
    public void A_tuple_is_returned_as_several_values()
    {
        string point = Flat(ScriptApiHarness.Run(ScriptApiFixtures.Representative).SourceNamed("PointApi.ScriptApi.g.cs"));

        point.ShouldContain(
            "varresult=global::TestGame.Scripting.PointApi.Unpack(self);" +
            "returnstack.Push((double)(result.Item1))&&stack.Push((double)(result.Item2))&&stack.Push((double)(result.Item3))?3:");
    }

    [Fact]
    public void The_description_has_the_words_of_the_summary_and_the_shape_of_the_method()
    {
        string node = ScriptApiHarness.Run(ScriptApiFixtures.Representative).SourceNamed("NodeApi.ScriptApi.g.cs");

        node.ShouldContain("Name = \"FindFirstChild\", Summary = \"The child of that name, or nil.\"");
        node.ShouldContain(
            "ScriptSignature([new global::SpectraEngine.Core.Scripting.Api.ScriptParameter(\"name\", \"string\"), " +
            "new global::SpectraEngine.Core.Scripting.Api.ScriptParameter(\"recursive\", \"boolean?\")], \"Instance?\")");
        node.ShouldContain("ChildType = \"Instance\",");
    }

    [Fact]
    public void What_is_bound_by_hand_is_described_and_not_installed()
    {
        string globals = ScriptApiHarness.Run(ScriptApiFixtures.Representative).SourceNamed("LevelGlobals.ScriptApi.g.cs");

        globals.ShouldContain("Name = \"script\", Type = \"Instance\"");
        globals.ShouldContain("Name = \"warn\"");
        globals.ShouldNotContain("\"script\"u8");
        globals.ShouldNotContain("\"warn\"u8");
        globals.ShouldContain("context.SetGlobalObject(\"lamp\"u8, this.Lamp);");
        globals.ShouldContain("context.SetGlobalFunction(\"wait\"u8, stack => this.Wait(stack));");
    }

    [Fact]
    public void A_summary_is_read_from_the_comment_with_its_tags_taken_out()
    {
        const string source = ScriptApiFixtures.Usings + """
            /// <summary>
            /// Counts <see cref="Vector3"/> values, and <c>nil</c> too,
            /// when <paramref name="self"/> has &lt; 3.
            /// </summary>
            [ScriptValue("Counter")]
            internal static partial class CounterApi
            {
                /// <summary>Its   length.</summary>
                /// <returns>Never read.</returns>
                [ScriptProperty]
                public static float Length(Vector3 self) => self.Length();
            }
            """;

        GeneratorRun run = ScriptApiHarness.Run(source);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        string counter = run.SourceNamed("CounterApi.ScriptApi.g.cs");
        counter.ShouldContain("Summary = \"Counts Vector3 values, and nil too, when self has < 3.\",");
        counter.ShouldContain("Summary = \"Its length.\"");
    }

    // Without the layout, so the lines of one statement can be read as one.
    private static string Flat(string source) => string.Concat(source.Where(c => !char.IsWhiteSpace(c)));
}
