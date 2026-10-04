using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Packs;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The shader cook: source in, one blob per requested backend out, and nothing
/// for a backend nobody asked for.
/// </summary>
// Fixture is the engine's ShadowDepth: two stages, a vertex input table and
// the generated instanced twin.
public class ShaderRuleTests
{
    private const string ShaderPath = "Shaders/ShadowDepth.spectrashade";
    private const string CookedPath = "Shaders/ShadowDepth.specshadecomp";

    [Fact]
    public void A_d3d11_only_pack_carries_no_gl_blob_and_still_loads()
    {
        using var project = new TempProject();
        WriteShader(project);

        var pack = project.Track(new PackSource(NullLogger.Instance, Cook(project, GraphicsBackend.D3D11)));

        pack.TryOpen(CookedPath, out ContentBlob? blob).ShouldBeTrue();
        using (blob)
        {
            PipelineBlob d3d11 = ShaderFileReader
                .ReadPipeline(blob.Span, GraphicsBackend.D3D11).ShouldNotBeNull();

            d3d11.VertexData.ShouldNotBeNull();
            d3d11.VertexInputs.ShouldNotBeEmpty();

            // The instanced twin survives the cook.
            d3d11.InstancedVertexData.ShouldNotBeNull();
            d3d11.InstancedVertexInputs.ShouldContain(
                element => element.Rate == VertexInputRate.PerInstance);

            ShaderFileReader.ReadPipeline(blob.Span, GraphicsBackend.OpenGL).ShouldBeNull();
            ShaderFileReader.ReadBackends(blob.Span).ShouldBe([GraphicsBackend.D3D11]);
        }

        var stack = new ContentSourceStack();
        stack.Mount(pack);

        ResolvedShader resolved = BaseShaderResolver.ResolveBuiltIn(
            stack, BaseShaders.ShadowDepthFileName, GraphicsBackend.D3D11, NullLogger.Instance);

        resolved.Cooked.ShouldNotBeNull();
        resolved.Source.ShouldBeNull();

        // A backend the pack was not cooked for falls back to source.
        BaseShaderResolver
            .ResolveBuiltIn(stack, BaseShaders.ShadowDepthFileName, GraphicsBackend.OpenGL, NullLogger.Instance)
            .Cooked.ShouldBeNull();
    }

    [Fact]
    public void The_pipeline_table_is_written_in_the_order_the_targets_were_asked_for()
    {
        using var project = new TempProject();
        WriteShader(project);

        var pack = project.Track(new PackSource(
            NullLogger.Instance, Cook(project, GraphicsBackend.D3D12, GraphicsBackend.OpenGL)));

        pack.TryOpen(CookedPath, out ContentBlob? blob).ShouldBeTrue();
        using (blob)
        {
            // Target order, not the compiler's registration order.
            ShaderFileReader.ReadBackends(blob.Span)
                .ShouldBe([GraphicsBackend.D3D12, GraphicsBackend.OpenGL]);
        }
    }

    [Fact]
    public void Two_clean_cooks_of_a_shader_in_two_processes_are_byte_identical()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteShader(project);

        // Two processes: the string hash seed is per process, so a leaked
        // dictionary order only shows across runs.
        byte[] first = CookOutOfProcess(project, "shader-a");
        byte[] second = CookOutOfProcess(project, "shader-b");

        second.ShouldBe(first);
    }

    [Fact]
    public void A_pack_whose_shaders_all_carry_the_same_backends_verifies_clean()
    {
        using var project = new TempProject();
        WriteShader(project);
        project.WriteAsset("Shaders/Lit.spectrashade", BaseShaders.Lit);

        PackVerifyResult result = PackVerifier.Verify(
            Cook(project, GraphicsBackend.D3D11, GraphicsBackend.OpenGL));

        result.Succeeded.ShouldBeTrue(Describe(result));
        result.EntriesChecked.ShouldBe(2);
    }

    [Fact]
    public void The_verifier_fails_a_pack_one_shader_short_and_names_the_backend_and_the_shader()
    {
        // Built by hand: one cook gives every shader the same target list.
        using var project = new TempProject();
        string pack = Path.Combine(project.Root, "mixed.spack");

        var writer = new PackWriter();
        writer.Add(
            "Shaders/Complete.specshadecomp",
            PackEntryKind.Shader,
            CompileToBytes(GraphicsBackend.D3D11, GraphicsBackend.OpenGL));
        writer.Add(
            "Shaders/Short.specshadecomp",
            PackEntryKind.Shader,
            CompileToBytes(GraphicsBackend.D3D11));
        writer.WriteToFile(pack);

        PackVerifyResult result = PackVerifier.Verify(pack);

        result.Succeeded.ShouldBeFalse();

        CookDiagnostic missing = result.Diagnostics.Single(d => d.IsError);
        missing.Id.ToString().ShouldBe("SC6002");
        missing.Message.ShouldContain("Shaders/Short.specshadecomp");
        missing.Message.ShouldContain("opengl");

        missing.Message.ShouldNotContain("Complete");
    }

    [Fact]
    public void A_named_target_list_is_authoritative_where_the_pack_alone_cannot_be()
    {
        using var project = new TempProject();
        WriteShader(project);

        string pack = Cook(project, GraphicsBackend.D3D11);

        // A pack does not record its targets, so with none given the expectation
        // is the union over its own shaders, here {d3d11}.
        PackVerifier.Verify(pack).Succeeded.ShouldBeTrue();

        PackVerifyResult asked = PackVerifier.Verify(
            pack, logger: null, targets: [GraphicsBackend.D3D11, GraphicsBackend.D3D12]);

        asked.Succeeded.ShouldBeFalse();

        CookDiagnostic missing = asked.Diagnostics.Single(d => d.IsError);
        missing.Id.ToString().ShouldBe("SC6002");
        missing.Message.ShouldContain("d3d12");
        missing.Message.ShouldContain("Shaders/ShadowDepth.specshadecomp");
    }

    [Fact]
    public void A_shader_the_compiler_refuses_reports_its_message_and_emits_nothing()
    {
        using var project = new TempProject();
        project.WriteAsset(ShaderPath, "this is not a shader at all {{{\n");

        CookResult result = new CookSession(
            project.Layout, new CookSettings { UseCache = false }).Run();

        result.Succeeded.ShouldBeFalse();

        // SC6001: the compiler's own diagnostics carry no code to wrap.
        result.Diagnostics.ShouldContain(d => d.IsError && d.Id.ToString() == "SC6001");

        result.Diagnostics
            .First(d => d.Id.ToString() == "SC6001")
            .File.ShouldBe(ShaderPath);

        // The shader must not fall back to a raw copy of its source.
        result.Assets
            .Single(a => a.SourcePath == ShaderPath)
            .Outputs.ShouldBeEmpty();
    }

    [Fact]
    public void A_shader_is_cooked_rather_than_copied()
    {
        using var project = new TempProject();
        WriteShader(project);

        CookResult result = new CookSession(
            project.Layout, new CookSettings { UseCache = false }).Run();

        result.Succeeded.ShouldBeTrue();

        CookedAsset asset = result.Assets.Single();
        asset.Rule.ShouldBe(Rules.RuleKind.Shader);

        // Only the blob, not the source.
        CookedOutput output = asset.Outputs.Single();
        output.Path.ShouldBe(CookedPath);
    }

    [Fact]
    public void A_cached_shader_cook_produces_the_bytes_the_clean_one_did()
    {
        using var project = new TempProject();
        WriteShader(project);

        string cold = Path.Combine(project.Root, "cold");
        string warm = Path.Combine(project.Root, "warm");

        new CookSession(project.Layout, new CookSettings { OutputPath = cold }).Run()
            .Succeeded.ShouldBeTrue();

        CookResult second = new CookSession(project.Layout, new CookSettings { OutputPath = warm }).Run();

        second.Succeeded.ShouldBeTrue();
        second.CacheHits.ShouldBe(1);

        ReadPack(cold).ShouldBe(ReadPack(warm));
    }

    [Fact]
    public void Changing_the_target_list_re_cooks_the_shader()
    {
        using var project = new TempProject();
        WriteShader(project);

        new CookSession(
                project.Layout,
                new CookSettings { OutputPath = Path.Combine(project.Root, "one"), Targets = [GraphicsBackend.D3D11] })
            .Run()
            .Succeeded.ShouldBeTrue();

        CookResult second = new CookSession(
                project.Layout,
                new CookSettings
                {
                    OutputPath = Path.Combine(project.Root, "two"),
                    Targets = [GraphicsBackend.D3D11, GraphicsBackend.OpenGL],
                })
            .Run();

        second.Succeeded.ShouldBeTrue();

        // A cache hit here would serve a d3d11-only blob to a two-backend cook.
        second.CacheHits.ShouldBe(0);
    }

    private static void WriteShader(TempProject project) =>
        project.WriteAsset(ShaderPath, BaseShaders.ShadowDepth);

    private static string Cook(TempProject project, params GraphicsBackend[] targets)
    {
        CookResult result = new CookSession(
            project.Layout,
            new CookSettings { UseCache = false, Targets = targets }).Run();

        result.Succeeded.ShouldBeTrue(Describe(result.Diagnostics));
        return result.OutputPath!;
    }

    private static byte[] CookOutOfProcess(TempProject project, string label)
    {
        string output = Path.Combine(project.Root, label);

        ScookProcess.Result run = ScookProcess.Run("cook", project.Root, "-o", output, "--no-cache");
        run.ExitCode.ShouldBe(0, $"scook failed: {run.Stderr}");

        return File.ReadAllBytes(Directory.GetFiles(output, "*.spack").Single());
    }

    private static byte[] ReadPack(string outputDirectory) =>
        File.ReadAllBytes(Directory.GetFiles(outputDirectory, "*.spack").Single());

    private static byte[] CompileToBytes(params GraphicsBackend[] targets)
    {
        CompiledShaderFile compiled = new SpectraShadeCompiler().Compile(BaseShaders.ShadowDepth, targets);

        using var bytes = new MemoryStream();
        ShaderFileWriter.Write(bytes, compiled);
        return bytes.ToArray();
    }

    private static string Describe(PackVerifyResult result) => Describe(result.Diagnostics);

    private static string Describe(IReadOnlyList<CookDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static d => d.ToString()));
}
