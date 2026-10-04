using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler.Analysis;
using SpectraShade.Compiler.CodeGen;
using SpectraShade.Compiler.Lexing;
using SpectraShade.Compiler.Syntax;

namespace SpectraShade.Compiler.Tests;

/// <summary>
/// Vertex inputs that advance once per instance, and the layout the compiled
/// output reports for them.
/// </summary>
// The rate is not in either target's shader text (GL: glVertexAttribDivisor,
// D3D: InputSlotClass), and a mat4 spans four locations. A wrong layout still
// compiles and draws, so the reported table is what gets tested.
public sealed class PerInstanceInputTests
{
    private const string Fixture = "InstancedVertex.spectrashade";

    [Fact]
    public void The_declared_inputs_are_reported_in_declaration_order()
    {
        PipelineBlob blob = CompileGlsl(Fixture);

        blob.VertexInputs.Select(v => v.Name)
            .ShouldBe(["position", "normal", "uv", "model", "tint"]);
    }

    [Fact]
    public void A_per_instance_field_is_reported_as_per_instance()
    {
        PipelineBlob blob = CompileGlsl(Fixture);

        Input(blob, "position").Rate.ShouldBe(VertexInputRate.PerVertex);
        Input(blob, "normal").Rate.ShouldBe(VertexInputRate.PerVertex);
        Input(blob, "uv").Rate.ShouldBe(VertexInputRate.PerVertex);
        Input(blob, "model").Rate.ShouldBe(VertexInputRate.PerInstance);
        Input(blob, "tint").Rate.ShouldBe(VertexInputRate.PerInstance);
    }

    [Fact]
    public void A_matrix_reports_four_locations_and_four_components_each()
    {
        // Four, not sixteen: components per row, which picks the element format.
        PipelineBlob blob = CompileGlsl(Fixture);
        VertexInputElement model = Input(blob, "model");

        model.Location.ShouldBe(3u);
        model.LocationSpan.ShouldBe(4u);
        model.ComponentCount.ShouldBe(4u);
        model.LocationEnd.ShouldBe(7u);
    }

    [Fact]
    public void A_vector_occupies_exactly_one_location()
    {
        PipelineBlob blob = CompileGlsl(Fixture);

        Input(blob, "position").LocationSpan.ShouldBe(1u);
        Input(blob, "position").ComponentCount.ShouldBe(3u);
        Input(blob, "uv").ComponentCount.ShouldBe(2u);
    }

    [Fact]
    public void The_field_after_a_matrix_starts_past_it()
    {
        PipelineBlob blob = CompileGlsl(Fixture);

        Input(blob, "tint").Location.ShouldBe(7u);
        Input(blob, "model").Overlaps(Input(blob, "tint")).ShouldBeFalse();
    }

    [Fact]
    public void Both_backends_report_the_same_layout()
    {
        PipelineBlob glsl = CompileGlsl(Fixture);
        PipelineBlob hlsl = CompileHlsl(Fixture);

        hlsl.VertexInputs.ShouldBe(glsl.VertexInputs);
    }

    [Fact]
    public void A_shader_with_no_per_instance_input_reports_all_per_vertex()
    {
        PipelineBlob blob = CompileGlsl("SimpleVertex.spectrashade");

        blob.VertexInputs.Count.ShouldBe(3);
        blob.VertexInputs.ShouldAllBe(v => v.Rate == VertexInputRate.PerVertex);
        blob.VertexInputs.ShouldAllBe(v => v.LocationSpan == 1);
    }

    [Fact]
    public void Glsl_declares_the_matrix_at_its_own_location()
    {
        // GLSL assigns 4, 5 and 6 itself. No rate in the text: the divisor carries it.
        string glsl = GlslStage(Fixture);

        glsl.ShouldContain("layout(location = 3) in mat4 a_model;", Case.Sensitive);
        glsl.ShouldContain("layout(location = 7) in vec4 a_tint;", Case.Sensitive);
        glsl.ShouldNotContain("PerInstance");
    }

    [Fact]
    public void Hlsl_gives_the_matrix_one_semantic_and_takes_four()
    {
        // float4x4 : TEXCOORD3 takes TEXCOORD3..6.
        string hlsl = HlslStage(Fixture);

        hlsl.ShouldContain("float4x4 model : TEXCOORD3;", Case.Sensitive);
        hlsl.ShouldContain("float4 tint : TEXCOORD7;", Case.Sensitive);
        hlsl.ShouldNotContain("PerInstance");
    }

    [Fact]
    public Task Glsl_vertex_stage_matches_snapshot() =>
        Verify(GlslStage(Fixture), extension: "vert");

    [Fact]
    public Task Hlsl_vertex_stage_matches_snapshot() =>
        Verify(HlslStage(Fixture), extension: "hlsl");

    [Fact]
    public void A_matrix_input_without_an_explicit_location_is_refused()
    {
        // Falling back to the field index, a mat4 at 1 would cover 1..4 and
        // the next field would land on 2.
        Diagnostic[] errors = Errors("""
            struct VertexInput {
                [Location(0)] vec3 position;
                mat4 model;
            }
            """);

        errors.ShouldContain(d => d.Message.Contains("occupies 4 locations"));
    }

    [Fact]
    public void A_per_instance_field_without_an_explicit_location_is_refused()
    {
        // A field-index default would put it on top of the per-vertex attributes.
        Diagnostic[] errors = Errors("""
            struct VertexInput {
                [Location(0)] vec3 position;
                [PerInstance] vec4 tint;
            }
            """);

        errors.ShouldContain(d => d.Message.Contains("needs an explicit [Location"));
    }

    [Fact]
    public void Two_fields_claiming_one_location_are_refused()
    {
        Diagnostic[] errors = Errors("""
            struct VertexInput {
                [Location(0)] vec3 position;
                [Location(0)] vec3 normal;
            }
            """);

        errors.ShouldContain(d => d.Message.Contains("overlaps"));
    }

    [Fact]
    public void A_field_landing_inside_a_matrix_is_refused()
    {
        // Location 5 is the third row of the matrix at 3.
        Diagnostic[] errors = Errors("""
            struct VertexInput {
                [Location(0)] vec3 position;
                [Location(3)][PerInstance] mat4 model;
                [Location(5)] vec2 uv;
            }
            """);

        errors.ShouldContain(d => d.Message.Contains("overlaps"));
    }

    [Fact]
    public void Per_instance_on_something_that_is_not_a_vertex_input_is_refused()
    {
        // Both generators would ignore it.
        Diagnostic[] errors = Errors(
            vertexInput: """
            struct VertexInput {
                [Location(0)] vec3 position;
            }
            """,
            extraStruct: """
            struct Extra {
                [PerInstance] vec4 tint;
            }
            """);

        errors.ShouldContain(d => d.Message.Contains("only valid on a vertex input"));
    }

    [Fact]
    public void A_type_that_cannot_be_a_vertex_input_is_refused()
    {
        Diagnostic[] errors = Errors("""
            struct VertexInput {
                [Location(0)] vec3 position;
                [Location(1)] sampler2D wrong;
            }
            """);

        errors.ShouldContain(d => d.Message.Contains("cannot be a vertex input"));
    }

    [Fact]
    public void The_engines_own_shaders_still_analyze_clean()
    {
        foreach (string fixture in new[]
                 {
                     "SimpleVertex.spectrashade",
                     "NestedReturns.spectrashade",
                     "GeometryExtrude.spectrashade",
                     "ArrayUniforms.spectrashade",
                 })
        {
            CompilationUnit unit = Parse(TestFixtures.Load(fixture));
            var analyzer = new SemanticAnalyzer();
            analyzer.Analyze(unit).ShouldBeTrue(fixture);
        }
    }

    // A marked uniform makes the compiler emit a second, instanced vertex stage.
    private const string Marked = """
        struct VertexInput {
            [Location(0)] vec3 position;
            [Location(1)] vec3 normal;
            [Location(2)] vec2 uv;
        }

        struct VertexOutput {
            [Position] vec4 position;
        }

        shader Marked {
            [Binding(0)] cbuffer Transforms {
                [PerInstance] mat4 uModel;
                mat4 uViewProjection;
            }

            [Vertex]
            VertexOutput VertexMain(VertexInput input) {
                var output = new VertexOutput();
                output.position = uViewProjection * uModel * vec4(input.position, 1.0);
                return output;
            }

            [Fragment] [Target(0)]
            vec4 FragmentMain(VertexOutput input) {
                return vec4(1.0, 1.0, 1.0, 1.0);
            }
        }
        """;

    [Fact]
    public void A_marked_uniform_produces_a_second_vertex_stage()
    {
        PipelineBlob blob = CompileSource(Marked);

        blob.VertexData.ShouldNotBeNull();
        blob.InstancedVertexData.ShouldNotBeNull();
        blob.InstancedVertexData.ShouldNotBe(blob.VertexData);
    }

    [Fact]
    public void A_shader_with_no_marked_uniform_produces_no_variant()
    {
        CompileSource(TestFixtures.Load("SimpleVertex.spectrashade"))
            .InstancedVertexData.ShouldBeNull();
    }

    [Fact]
    public void The_ordinary_stage_is_left_exactly_as_it_was()
    {
        // A single draw keeps the uniform path.
        string withMark = Stage(CompileSource(Marked).VertexData!);
        string withoutMark = Stage(CompileSource(Marked.Replace("[PerInstance] mat4 uModel;", "mat4 uModel;")).VertexData!);

        withMark.ShouldBe(withoutMark);
    }

    [Fact]
    public void The_variant_takes_the_matrix_as_a_vertex_input()
    {
        PipelineBlob blob = CompileSource(Marked);

        // 3 is the first free location past position, normal and uv.
        blob.InstancedVertexInputs.Count.ShouldBe(4);
        VertexInputElement model = blob.InstancedVertexInputs.First(v => v.Name == "uModel");
        model.Rate.ShouldBe(VertexInputRate.PerInstance);
        model.Location.ShouldBe(3u);
        model.LocationSpan.ShouldBe(4u);

        blob.VertexInputs.ShouldAllBe(v => v.Rate == VertexInputRate.PerVertex);
    }

    [Fact]
    public void The_variant_drops_the_uniform_but_keeps_its_neighbours()
    {
        string glsl = Stage(CompileSource(Marked).InstancedVertexData!);

        glsl.ShouldContain("layout(location = 3) in mat4 a_uModel;", Case.Sensitive);
        glsl.ShouldContain("uniform mat4 uViewProjection;", Case.Sensitive);
        glsl.ShouldNotContain("uniform mat4 uModel;", Case.Sensitive);
    }

    [Fact]
    public void The_variant_binds_the_name_so_the_body_is_untouched()
    {
        // A leading local keeps the body's bare references resolving, so no
        // expression is rewritten.
        string glsl = Stage(CompileSource(Marked).InstancedVertexData!);

        glsl.ShouldContain("mat4 uModel = a_uModel;", Case.Sensitive);
        glsl.ShouldContain("(uViewProjection * uModel)", Case.Sensitive);
    }

    [Fact]
    public void Hlsl_gets_the_same_treatment()
    {
        // The public compiler attaches the variant. A generator alone does not.
        PipelineBlob blob = new SpectraShadeCompiler()
            .Compile(Marked, [GraphicsBackend.D3D11])
            .GetPipeline(GraphicsBackend.D3D11)
            .ShouldNotBeNull();

        string hlsl = Stage(blob.InstancedVertexData.ShouldNotBeNull());

        hlsl.ShouldContain("float4x4 uModel : TEXCOORD3;", Case.Sensitive);
        hlsl.ShouldContain("float4x4 uModel = input.uModel;", Case.Sensitive);
    }

    [Fact]
    public void A_per_instance_uniform_that_is_not_a_matrix_is_refused()
    {
        // Span and stride would not match the instance layout.
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(Parse(Marked.Replace("[PerInstance] mat4 uModel;", "[PerInstance] vec3 uModel;")));

        analyzer.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("must be mat4"));
    }

    [Fact]
    public void A_second_per_instance_uniform_is_refused()
    {
        // InstancedVariant would take the first and ignore the rest.
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(Parse(Marked.Replace(
            "mat4 uViewProjection;", "[PerInstance] mat4 uOther; mat4 uViewProjection;")));

        analyzer.Diagnostics.ShouldContain(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("a second"));
    }

    [Fact]
    public void The_engines_shadow_pass_carries_a_variant()
    {
        // ShadowDepth marks uModel; the renderer draws the generated twin.
        PipelineBlob blob = CompileSource(BaseShaders.ShadowDepth);

        blob.InstancedVertexData.ShouldNotBeNull();
        blob.InstancedVertexInputs.ShouldContain(v =>
            v.Name == "uModel" && v.Rate == VertexInputRate.PerInstance && v.LocationSpan == 4);
    }

    private static VertexInputElement Input(PipelineBlob blob, string name) =>
        blob.VertexInputs.First(v => v.Name == name);

    private static CompilationUnit Parse(string source)
    {
        var parser = new Parser(new Lexer(source, "test.spectrashade").Tokenize());
        CompilationUnit unit = parser.Parse();
        parser.Diagnostics.ShouldBeEmpty();
        return unit;
    }

    private static PipelineBlob CompileGlsl(string fixtureName) =>
        new GlslGenerator().Generate(Analyzed(fixtureName));

    private static PipelineBlob CompileHlsl(string fixtureName) =>
        new HlslGenerator(GraphicsBackend.D3D11).Generate(Analyzed(fixtureName));

    private static CompilationUnit Analyzed(string fixtureName)
    {
        CompilationUnit unit = Parse(TestFixtures.Load(fixtureName));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(unit).ShouldBeTrue();
        return unit;
    }

    // The generators emit Environment.NewLine; the assertions expect LF.
    private static string Stage(byte[] data) =>
        Encoding.UTF8.GetString(data).ReplaceLineEndings("\n");

    private static CompilationUnit AnalyzedSource(string source)
    {
        CompilationUnit unit = Parse(source);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(unit).ShouldBeTrue();
        return unit;
    }

    private static PipelineBlob CompileSource(string source) =>
        new GlslGenerator().Generate(AnalyzedSource(source)) is var glsl
            && InstancedVariant.TryBuild(AnalyzedSource(source), out CompilationUnit? instanced)
            ? InstancedBlob.With(glsl, new GlslGenerator().Generate(instanced))
            : glsl;

    private static string GlslStage(string fixtureName) =>
        Encoding.UTF8.GetString(CompileGlsl(fixtureName).VertexData!).Replace("\r\n", "\n");

    private static string HlslStage(string fixtureName) =>
        Encoding.UTF8.GetString(CompileHlsl(fixtureName).VertexData!).Replace("\r\n", "\n");

    // Wraps a vertex input struct in the smallest shader that analyzes.
    private static Diagnostic[] Errors(string vertexInput, string? extraStruct = null)
    {
        string source = $$"""
            {{vertexInput}}

            {{extraStruct ?? ""}}

            struct VertexOutput {
                [Position] vec4 position;
            }

            shader Probe {
                [Vertex]
                VertexOutput VertexMain(VertexInput input) {
                    var output = new VertexOutput();
                    output.position = vec4(1.0, 1.0, 1.0, 1.0);
                    return output;
                }

                [Fragment] [Target(0)]
                vec4 FragmentMain(VertexOutput input) {
                    return vec4(1.0, 1.0, 1.0, 1.0);
                }
            }
            """;

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(Parse(source));
        return [.. analyzer.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];
    }
}
