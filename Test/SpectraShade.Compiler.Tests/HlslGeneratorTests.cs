using System.Text;
using System.Threading.Tasks;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler.Analysis;
using SpectraShade.Compiler.CodeGen;
using SpectraShade.Compiler.Lexing;
using SpectraShade.Compiler.Syntax;

namespace SpectraShade.Compiler.Tests;

public sealed class HlslGeneratorTests
{
    [Fact]
    public Task Vertex_stage_matches_snapshot() => VerifyStage(ShaderStage.Vertex);

    [Fact]
    public Task Fragment_stage_matches_snapshot() => VerifyStage(ShaderStage.Fragment);

    [Fact]
    public Task Geometry_stage_matches_snapshot()
    {
        var blob = Compile("GeometryExtrude.spectrashade");
        blob.GeometryData.ShouldNotBeNull();
        var text = Encoding.UTF8.GetString(blob.GeometryData!);
        return Verify(text, extension: "hlsl");
    }

    [Fact]
    public void For_loop_expression_initializer_is_kept()
    {
        var hlsl = CompileStageText("ForExpressionInitializer.spectrashade", ShaderStage.Fragment);

        // The parser unwraps `(j = 0)`, so both spellings emit the same.
        hlsl.ShouldContain("for (i = 0; (i < 4); i = (i + 1))", Case.Sensitive);
        hlsl.ShouldContain("for (j = 0; (j < 2); j = (j + 1))", Case.Sensitive);
    }

    [Fact]
    public void Vertex_stage_returning_bare_vec4_declares_SV_Position()
    {
        var hlsl = CompileStageText("BareVertexReturn.spectrashade", ShaderStage.Vertex);

        // No struct field to carry the semantic, and FXC/DXC reject a vertex
        // entry without SV_Position.
        hlsl.ShouldContain("float4 main(VertexInput input) : SV_Position", Case.Sensitive);
    }

    private static SettingsTask VerifyStage(ShaderStage stage)
    {
        var blob = Compile("SimpleVertex.spectrashade");
        var data = stage switch
        {
            ShaderStage.Vertex => blob.VertexData,
            ShaderStage.Fragment => blob.FragmentData,
            _ => null,
        };
        data.ShouldNotBeNull();
        var text = Encoding.UTF8.GetString(data!);
        return Verify(text, extension: "hlsl");
    }

    [Fact]
    public void Whole_number_float_literals_keep_their_decimal_point()
    {
        // Keeps the HLSL output in step with the GLSL test of the same name.
        var hlsl = CompileStageText("WholeNumberFloats.spectrashade", ShaderStage.Fragment);

        hlsl.ShouldContain("float third = (1.0 / 3.0);", Case.Sensitive);
        hlsl.ShouldNotContain("(1 / 3)", Case.Sensitive);
    }

    [Fact]
    public void Array_uniforms_land_inside_their_cbuffer()
    {
        // The runtime reflects member offsets out of this cbuffer.
        var hlsl = CompileStageText("ArrayUniforms.spectrashade", ShaderStage.Fragment);

        hlsl.ShouldContain("cbuffer Lights : register(b0)", Case.Sensitive);
        hlsl.ShouldContain("float4 uLightPositions[4];", Case.Sensitive);
        hlsl.ShouldContain("float4 uLightColors[4];", Case.Sensitive);
    }

    [Fact]
    public void A_matrix_array_gets_its_own_register()
    {
        // One GPU buffer per register on D3D: sharing with Lights would
        // re-upload the array on every draw.
        var hlsl = CompileStageText("ArrayUniforms.spectrashade", ShaderStage.Vertex);

        hlsl.ShouldContain("cbuffer Cascades : register(b1)", Case.Sensitive);
        hlsl.ShouldContain("float4x4 uCascadeMatrices[2];", Case.Sensitive);
    }

    private static string CompileStageText(string fixtureName, ShaderStage stage)
    {
        var blob = Compile(fixtureName);
        var data = stage switch
        {
            ShaderStage.Vertex => blob.VertexData,
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

        return new HlslGenerator(GraphicsBackend.D3D11).Generate(unit);
    }

    private enum ShaderStage { Vertex, Fragment }
}
