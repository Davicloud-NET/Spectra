namespace SpectraEngine.Entities.Tests;

// The kinds of value the door takes beside a string, a Vector3 and a CFrame:
// a boolean, a number and a Color3, each on a class below Instance. And a
// Color3 as a value a script holds.
public sealed class ScriptApiGeneratorDoorKindTests
{
    private const string Atoms = "global::SpectraEngine.Scripting.Luau.Runtime.ScriptAtoms";
    private const string Door = "global::SpectraEngine.Core.Scripting";
    private const string ColorValue = "global::SpectraEngine.Scripting.Luau.Runtime.ColorValue";
    private const string Arguments = "global::SpectraEngine.Scripting.Luau.Runtime.ScriptArguments";
    private const string Color = "global::SpectraEngine.Core.Scene.Color3";

    private const string Lamps = ScriptApiFixtures.Usings + """
        /// <summary>A node of the level.</summary>
        [ScriptClass("Instance")]
        internal static partial class NodeApi
        {
            /// <summary>Its name.</summary>
            [ScriptProperty(Sets = SettableMemberId.Name)]
            public static partial string Name(SceneNode node);
        }

        /// <summary>A node that lights the level.</summary>
        [ScriptClass("Light", Extends = "Instance")]
        internal static partial class LightApi
        {
            /// <summary>Whether it shines.</summary>
            [ScriptProperty(Sets = SettableMemberId.Enabled)]
            public static partial bool Enabled(SceneNode node);

            /// <summary>How bright it is.</summary>
            [ScriptProperty(Sets = SettableMemberId.Brightness)]
            public static partial float Brightness(SceneNode node);

            /// <summary>Its colour.</summary>
            [ScriptProperty(Sets = SettableMemberId.Color)]
            internal static partial Color3 Color(SceneNode node);
        }

        /// <summary>A light that shines from a point.</summary>
        [ScriptClass("PointLight", Extends = "Light")]
        internal static partial class BulbApi
        {
            /// <summary>How far it reaches.</summary>
            [ScriptProperty(Sets = SettableMemberId.Range)]
            public static partial double Range(SceneNode node);
        }

        /// <summary>A colour.</summary>
        [ScriptValue("Color3")]
        internal static partial class Color3Api
        {
            /// <summary>Its red.</summary>
            [ScriptProperty]
            public static float R(Color3 self) => self.R;

            /// <summary>Part of the way to the goal.</summary>
            [ScriptMethod]
            public static Color3 Lerp(Color3 self, Color3 goal, float alpha) => self.Lerp(goal, alpha);

            /// <summary>Mixed with another, or with none.</summary>
            [ScriptMethod]
            public static Color3? Mixed(Color3 self, Color3? other = null) => other;
        }

        /// <summary>Makes colours.</summary>
        [ScriptLibrary("Color3")]
        internal static partial class Color3Library
        {
            /// <summary>White.</summary>
            [ScriptProperty(Name = "white")]
            public static Color3 White => new(1f, 1f, 1f);

            /// <summary>Makes a colour.</summary>
            [ScriptFunction(Name = "new")]
            public static Color3 New(float r = 0f, float g = 0f, float b = 0f) => new(r, g, b);
        }
        """;

    [Fact]
    public void Settable_properties_of_every_kind_below_Instance_are_bound_and_compile()
    {
        GeneratorRun run = ScriptApiHarness.Run(Lamps);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("LightApi", "Enabled", "WriteBoolean")]
    [InlineData("LightApi", "Brightness", "WriteNumber")]
    [InlineData("LightApi", "Color", "WriteColor")]
    [InlineData("BulbApi", "Range", "WriteNumber")]
    public void A_write_goes_to_the_doors_writer_for_its_kind(string owner, string name, string writer)
    {
        string source = Flat(ScriptApiHarness.Run(Lamps).SourceNamed($"{owner}.ScriptApi.g.cs"));

        source.ShouldContain($"case{Atoms}.{name}:returncontext.{writer}(stack,self,{Door}.SettableMemberId.{name});");
    }

    [Theory]
    [InlineData("LightApi", "publicstaticpartialboolEnabled(", "Enabled).Boolean;")]
    [InlineData("LightApi", "publicstaticpartialfloatBrightness(", "(float)" + Door + ".ScriptWrites.Read(node," + Door + ".SettableMemberId.Brightness).Number;")]
    [InlineData("LightApi", "internalstaticpartial" + Color + "Color(", "Color).Color;")]
    [InlineData("BulbApi", "publicstaticpartialdoubleRange(", "(double)" + Door + ".ScriptWrites.Read(node," + Door + ".SettableMemberId.Range).Number;")]
    public void A_read_comes_from_the_door_as_the_type_the_property_has(string owner, string declared, string read)
    {
        string source = Flat(ScriptApiHarness.Run(Lamps).SourceNamed($"{owner}.ScriptApi.g.cs"));

        source.ShouldContain(declared);
        source.ShouldContain(read);
    }

    [Fact]
    public void A_class_hands_a_name_it_does_not_declare_up_the_chain_to_Instance()
    {
        GeneratorRun run = ScriptApiHarness.Run(Lamps);
        string point = Flat(run.SourceNamed("BulbApi.ScriptApi.g.cs"));
        string catalog = Flat(run.SourceNamed("ScriptApiCatalog.g.cs"));

        point.ShouldContain("default:returnglobal::SpectraEngine.Scripting.Luau.Runtime.ScriptBases.Write_TestGame_Scripting_BulbApi(context,stack,self,atom);");
        catalog.ShouldContain("Write_TestGame_Scripting_BulbApi(");
        catalog.ShouldContain("global::TestGame.Scripting.LightApi.ScriptWrite(context,stack,self,atom);");
        catalog.ShouldContain("global::TestGame.Scripting.NodeApi.ScriptWrite(context,stack,self,atom);");
    }

    [Fact]
    public void A_Color3_is_pushed_and_read_by_its_own_value_and_reader()
    {
        string color = Flat(ScriptApiHarness.Run(Lamps).SourceNamed("Color3Api.ScriptApi.g.cs"));

        color.ShouldContain($"{Arguments}.TryColor(stack,2,out{Color}a0)");
        color.ShouldContain($"{ColorValue}.Push(stack,");
        color.ShouldContain($"is{{}}pushed0?{ColorValue}.Push(stack,pushed0):stack.PushNil()");
        color.ShouldContain($"{Arguments}.TryColor(stack,1,out{Color}self)");
    }

    [Fact]
    public void A_Color3_constant_of_a_library_is_put_into_its_table_as_a_colour()
    {
        string library = Flat(ScriptApiHarness.Run(Lamps).SourceNamed("Color3Library.ScriptApi.g.cs"));

        library.ShouldContain(".Color(\"white\"u8,global::TestGame.Scripting.Color3Library.White)");
    }

    [Fact]
    public void The_description_names_the_doors_member_and_the_type_a_script_reads()
    {
        GeneratorRun run = ScriptApiHarness.Run(Lamps);
        string light = run.SourceNamed("LightApi.ScriptApi.g.cs");

        light.ShouldContain($"Name = \"Enabled\", Type = \"boolean\", Summary = \"Whether it shines.\", Sets = {Door}.SettableMemberId.Enabled, ");
        light.ShouldContain($"Name = \"Brightness\", Type = \"number\", Summary = \"How bright it is.\", Sets = {Door}.SettableMemberId.Brightness, ");
        light.ShouldContain($"Name = \"Color\", Type = \"Color3\", Summary = \"Its colour.\", Sets = {Door}.SettableMemberId.Color, ");
        run.SourceNamed("Color3Api.ScriptApi.g.cs").ShouldContain("new global::SpectraEngine.Core.Scripting.Api.ScriptParameter(\"goal\", \"Color3\")");
    }

    // Without the layout, so the lines of one statement can be read as one.
    private static string Flat(string source) => string.Concat(source.Where(c => !char.IsWhiteSpace(c)));
}
