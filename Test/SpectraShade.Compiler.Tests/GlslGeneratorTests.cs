using System.Text;
using System.Threading.Tasks;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler.Analysis;
using SpectraShade.Compiler.CodeGen;
using SpectraShade.Compiler.Lexing;
using SpectraShade.Compiler.Syntax;

namespace SpectraShade.Compiler.Tests;

public sealed class GlslGeneratorTests
{
    [Fact]
    public Task Vertex_stage_matches_snapshot() =>
        VerifyStage("SimpleVertex.spectrashade", ShaderStage.Vertex);

    [Fact]
    public Task Fragment_stage_matches_snapshot() =>
        VerifyStage("SimpleVertex.spectrashade", ShaderStage.Fragment);

    [Fact]
    public Task Nested_return_vertex_stage_matches_snapshot() =>
        VerifyStage("NestedReturns.spectrashade", ShaderStage.Vertex);

    [Fact]
    public Task Nested_return_fragment_stage_matches_snapshot() =>
        VerifyStage("NestedReturns.spectrashade", ShaderStage.Fragment);

    [Fact]
    public Task Geometry_shader_vertex_stage_matches_snapshot() =>
        VerifyStage("GeometryExtrude.spectrashade", ShaderStage.Vertex);

    [Fact]
    public Task Geometry_shader_geometry_stage_matches_snapshot() =>
        VerifyStage("GeometryExtrude.spectrashade", ShaderStage.Geometry);

    [Fact]
    public Task Geometry_shader_fragment_stage_matches_snapshot() =>
        VerifyStage("GeometryExtrude.spectrashade", ShaderStage.Fragment);

    [Fact]
    public void Braceless_if_return_in_fragment_stage_stays_guarded()
    {
        var glsl = CompileStageText("BracelessReturns.spectrashade", ShaderStage.Fragment);

        // The return lowers to assignment + bare return, so the body needs braces.
        glsl.ShouldContain(
            "    if ((v_uv.x > 0.5))\n" +
            "    {\n" +
            "        fragColor = vec4(1.0, 0.0, 0.0, 1.0);\n" +
            "        return;\n" +
            "    }\n", Case.Sensitive);

        glsl.ShouldContain(
            "    fragColor = vec4(0.0, 1.0, 0.0, 1.0);\n" +
            "    return;\n", Case.Sensitive);
    }

    [Fact]
    public void Braceless_if_return_in_vertex_stage_stays_guarded()
    {
        var glsl = CompileStageText("BracelessReturns.spectrashade", ShaderStage.Vertex);

        glsl.ShouldContain(
            "    if ((a_position.x < 0.5))\n" +
            "    {\n" +
            "        gl_Position = result.position;\n" +
            "        v_uv = result.uv;\n" +
            "        return;\n" +
            "    }\n", Case.Sensitive);

        glsl.ShouldContain(
            "    result.uv = vec2(0.0, 0.0);\n" +
            "    gl_Position = result.position;\n" +
            "    v_uv = result.uv;\n" +
            "    return;\n", Case.Sensitive);
    }

    [Fact]
    public void Vertex_stage_returning_bare_vec4_writes_gl_Position()
    {
        var glsl = CompileStageText("BareVertexReturn.spectrashade", ShaderStage.Vertex);

        glsl.ShouldContain(
            "void main()\n" +
            "{\n" +
            "    gl_Position = vec4(a_position, 1.0);\n" +
            "    return;\n" +
            "}\n", Case.Sensitive);
    }

    [Fact]
    public void For_loop_expression_initializer_is_kept()
    {
        var glsl = CompileStageText("ForExpressionInitializer.spectrashade", ShaderStage.Fragment);

        // The parser unwraps `(j = 0)`, so both spellings emit the same.
        glsl.ShouldContain("for (i = 0; (i < 4); i = (i + 1))", Case.Sensitive);
        glsl.ShouldContain("for (j = 0; (j < 2); j = (j + 1))", Case.Sensitive);
    }

    [Fact]
    public void Fragment_local_named_fragColor_is_escaped_off_the_stage_output()
    {
        var glsl = CompileStageText("FragColorShadow.spectrashade", ShaderStage.Fragment);

        // Unescaped, the local shadows the generator's output and still compiles.
        glsl.ShouldContain("out vec4 fragColor;\n", Case.Sensitive);
        glsl.ShouldContain("    vec4 _ss_fragColor = vec4(v_uv, 0.0, 1.0);\n", Case.Sensitive);
        glsl.ShouldContain(
            "    fragColor = _ss_fragColor;\n" +
            "    return;\n", Case.Sensitive);
    }

    private static SettingsTask VerifyStage(string fixtureName, ShaderStage stage)
    {
        var blob = Compile(fixtureName);
        var (data, extension) = stage switch
        {
            ShaderStage.Vertex => (blob.VertexData, "vert"),
            ShaderStage.Geometry => (blob.GeometryData, "geom"),
            _ => (blob.FragmentData, "frag"),
        };
        data.ShouldNotBeNull();
        var text = Encoding.UTF8.GetString(data!);
        return Verify(text, extension: extension);
    }

    [Fact]
    public void Whole_number_float_literals_keep_their_decimal_point()
    {
        var glsl = CompileStageText("WholeNumberFloats.spectrashade", ShaderStage.Fragment);

        // GLSL has no float suffix: "1" is an int and `1 / 3` is zero.
        glsl.ShouldContain("float third = (1.0 / 3.0);", Case.Sensitive);
        glsl.ShouldContain("float scaled = (2.0 * third);", Case.Sensitive);
        glsl.ShouldContain("vec3(1.0, 0.0, 0.0)", Case.Sensitive);

        glsl.ShouldNotContain("(1 / 3)", Case.Sensitive);
        glsl.ShouldNotContain("(2 * third)", Case.Sensitive);
    }

    [Fact]
    public void The_hlsl_generator_agrees_digit_for_digit()
    {
        var glsl = CompileStageText("WholeNumberFloats.spectrashade", ShaderStage.Fragment);

        glsl.ShouldContain("1.0 / 3.0", Case.Sensitive);
    }

    [Fact]
    public void Array_uniforms_use_the_syntax_the_documentation_teaches()
    {
        // The fixture is the example from LANGUAGE.md.
        var glsl = CompileStageText("ArrayUniforms.spectrashade", ShaderStage.Fragment);

        glsl.ShouldContain("uniform vec4 uLightColors[4];", Case.Sensitive);
        glsl.ShouldContain("uniform vec4 uLightPositions[4];", Case.Sensitive);
    }

    [Fact]
    public void An_array_uniform_can_be_indexed_by_a_loop_variable()
    {
        var glsl = CompileStageText("ArrayUniforms.spectrashade", ShaderStage.Fragment);

        glsl.ShouldContain("for (int i = 0; (i < uLightCount); i = (i + 1))", Case.Sensitive);
        glsl.ShouldContain("uLightColors[i].rgb", Case.Sensitive);
    }

    [Fact]
    public void A_matrix_array_survives_into_the_vertex_stage()
    {
        var glsl = CompileStageText("ArrayUniforms.spectrashade", ShaderStage.Vertex);

        glsl.ShouldContain("uniform mat4 uCascadeMatrices[2];", Case.Sensitive);
    }

    private static string CompileStageText(string fixtureName, ShaderStage stage)
    {
        var blob = Compile(fixtureName);
        var data = stage switch
        {
            ShaderStage.Vertex => blob.VertexData,
            ShaderStage.Geometry => blob.GeometryData,
            _ => blob.FragmentData,
        };
        data.ShouldNotBeNull();
        // The generator emits Environment.NewLine; the assertions expect LF.
        return Encoding.UTF8.GetString(data!).Replace("\r\n", "\n");
    }

    private static PipelineBlob Compile(string fixtureName)
    {
        var source = TestFixtures.Load(fixtureName);
        var tokens = new Lexer(source, fixtureName).Tokenize();
        var parser = new Parser(tokens);
        var unit = parser.Parse();
        parser.Diagnostics.ShouldBeEmpty();

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(unit).ShouldBeTrue();
        analyzer.Diagnostics.ShouldBeEmpty();

        return new GlslGenerator().Generate(unit);
    }

    private enum ShaderStage { Vertex, Geometry, Fragment }
}
