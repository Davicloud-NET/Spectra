using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.IO;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

// Resolution order: cooked blob, then source in the content stack, then the
// embedded copy. A wrong order still renders the same frame, so only these
// tests can see it.
public class BaseShaderResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"spectra_shaders_{Guid.NewGuid():N}");

    public BaseShaderResolverTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void With_no_content_source_a_built_in_comes_from_the_engines_own_copy()
    {
        // The file in a developer tree, the embedded resource otherwise. Same text.
        ResolvedShader resolved = Resolve(content: null);

        resolved.Cooked.ShouldBeNull();
        resolved.Source.ShouldBe(BaseShaders.Lit);
    }

    [Fact]
    public void A_source_the_content_stack_holds_overrides_the_embedded_copy_and_is_watchable()
    {
        const string authored = "// a project's own lit shader\n";
        WriteContent("Shaders/Lit.spectrashade", authored);

        ResolvedShader resolved = Resolve(LooseStack());

        resolved.Cooked.ShouldBeNull();
        resolved.Source.ShouldBe(authored);

        // A loose file has a watch path; a packed one has none.
        resolved.WatchPath.ShouldNotBeNull();
        Path.GetFullPath(resolved.WatchPath).ShouldBe(
            Path.GetFullPath(Path.Combine(_root, "Shaders", "Lit.spectrashade")));
    }

    [Fact]
    public void A_cooked_blob_beats_the_source_beside_it()
    {
        WriteContent("Shaders/Lit.spectrashade", "// never compiled\n");
        WriteContent("Shaders/Lit.specshadecomp", CookedBytes(GraphicsBackend.D3D11));

        ResolvedShader resolved = Resolve(LooseStack(), GraphicsBackend.D3D11);

        resolved.Source.ShouldBeNull();
        resolved.Cooked.ShouldNotBeNull().Backend.ShouldBe(GraphicsBackend.D3D11);

        // A cooked blob is not watched: re-reading it when the source changes
        // would serve the old shader.
        resolved.WatchPath.ShouldBeNull();
    }

    [Fact]
    public void A_cooked_file_with_no_blob_for_this_backend_falls_back_to_source()
    {
        const string authored = "// compiled at runtime instead\n";
        WriteContent("Shaders/Lit.spectrashade", authored);
        WriteContent("Shaders/Lit.specshadecomp", CookedBytes(GraphicsBackend.D3D11));

        // Cooked for d3d11, run on GL: logged at Error, then compiled from source.
        ResolvedShader resolved = Resolve(LooseStack(), GraphicsBackend.OpenGL);

        resolved.Cooked.ShouldBeNull();
        resolved.Source.ShouldBe(authored);
    }

    [Fact]
    public void An_unreadable_cooked_file_degrades_rather_than_throwing()
    {
        const string authored = "// still renders\n";
        WriteContent("Shaders/Lit.spectrashade", authored);
        WriteContent("Shaders/Lit.specshadecomp", [1, 2, 3, 4, 5, 6, 7, 8]);

        // Content failures never reach the draw loop; the cooker is where this is fatal.
        ResolvedShader resolved = Resolve(LooseStack());

        resolved.Cooked.ShouldBeNull();
        resolved.Source.ShouldBe(authored);
    }

    [Fact]
    public void A_source_saved_with_a_byte_order_mark_is_decoded_without_it()
    {
        const string authored = "// saved by an editor that writes a BOM\n";

        var bytes = new byte[3 + Encoding.UTF8.GetByteCount(authored)];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        Encoding.UTF8.GetBytes(authored, bytes.AsSpan(3));

        WriteContent("Shaders/Lit.spectrashade", bytes);

        // Left in, U+FEFF is a syntax error on line one.
        Resolve(LooseStack()).Source.ShouldBe(authored);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Don't fail a test on its own cleanup.
        }
    }

    private ResolvedShader Resolve(
        IContentSource? content, GraphicsBackend backend = GraphicsBackend.OpenGL) =>
        BaseShaderResolver.ResolveBuiltIn(
            content, BaseShaders.LitFileName, backend, NullLogger.Instance);

    private ContentSourceStack LooseStack()
    {
        var stack = new ContentSourceStack();
        stack.Mount(new LooseFileSource(NullLogger.Instance, _root));
        return stack;
    }

    private void WriteContent(string relative, string text) =>
        WriteContent(relative, Encoding.UTF8.GetBytes(text));

    private void WriteContent(string relative, byte[] bytes)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    // Written by the engine's own writer, so the resolver reads the real format.
    private static byte[] CookedBytes(GraphicsBackend backend)
    {
        var file = new CompiledShaderFile
        {
            FormatVersion = EngineInfo.ShaderFormatVersion,
            Stages = ShaderStageFlags.Vertex | ShaderStageFlags.Fragment,
            Pipelines =
            [
                new PipelineBlob
                {
                    Backend = backend,
                    Format = ShaderDataFormat.SourceText,
                    Stages = ShaderStageFlags.Vertex | ShaderStageFlags.Fragment,
                    VertexData = Encoding.UTF8.GetBytes("vertex"),
                    FragmentData = Encoding.UTF8.GetBytes("fragment"),
                    VertexInputs = [new VertexInputElement("position", 0, 1, 3, VertexInputRate.PerVertex)],
                },
            ],
        };

        using var bytes = new MemoryStream();
        ShaderFileWriter.Write(bytes, file);
        return bytes.ToArray();
    }
}
