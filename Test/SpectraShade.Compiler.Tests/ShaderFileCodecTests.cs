using System;
using System.IO;
using System.Text;
using SpectraEngine.Core;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;

namespace SpectraShade.Compiler.Tests;

/// <summary>
/// The .specshadecomp container: what survives a round trip, how another format
/// version is handled, and whether the stream and span readers agree.
/// </summary>
// Only a cooked pack reads these files, and a dropped input table or instanced
// stage there does not throw.
public sealed class ShaderFileCodecTests
{
    [Fact]
    public void A_pipeline_written_to_disk_round_trips_its_vertex_inputs_and_instanced_variant()
    {
        // ShadowDepth marks uModel, so its blob has the instanced stage and table.
        CompiledShaderFile compiled = new SpectraShadeCompiler()
            .Compile(BaseShaders.ShadowDepth, [GraphicsBackend.OpenGL]);

        PipelineBlob source = compiled.GetPipeline(GraphicsBackend.OpenGL).ShouldNotBeNull();
        source.VertexInputs.ShouldNotBeEmpty();
        source.InstancedVertexData.ShouldNotBeNull();
        source.InstancedVertexInputs.ShouldNotBeEmpty();

        byte[] bytes = WriteToBytes(compiled);

        PipelineBlob whole = ShaderFileReader.Read(new MemoryStream(bytes))
            .GetPipeline(GraphicsBackend.OpenGL).ShouldNotBeNull();

        // The path Renderer.LoadCompiledShader takes.
        PipelineBlob partial = ShaderFileReader
            .ReadPipeline(new MemoryStream(bytes), GraphicsBackend.OpenGL)
            .ShouldNotBeNull();

        foreach (PipelineBlob read in new[] { whole, partial })
        {
            read.VertexInputs.ShouldBe(source.VertexInputs);
            read.InstancedVertexInputs.ShouldBe(source.InstancedVertexInputs);
            read.InstancedVertexData.ShouldBe(source.InstancedVertexData);
            read.VertexData.ShouldBe(source.VertexData);
            read.FragmentData.ShouldBe(source.FragmentData);
        }
    }

    [Fact]
    public void A_shader_with_no_instanced_variant_reads_back_without_one()
    {
        PipelineBlob source = Blob(GraphicsBackend.OpenGL, instanced: false);
        byte[] bytes = WriteToBytes(File(source));

        PipelineBlob read = ShaderFileReader
            .ReadPipeline(new MemoryStream(bytes), GraphicsBackend.OpenGL)
            .ShouldNotBeNull();

        read.InstancedVertexData.ShouldBeNull();
        read.InstancedVertexInputs.ShouldBeEmpty();
        read.VertexInputs.ShouldBe(source.VertexInputs);
    }

    [Fact]
    public void A_shader_file_with_a_different_format_version_is_refused_naming_both_versions()
    {
        const ushort bogus = 9;
        byte[] bytes = HeaderOnly(bogus);

        var whole = Should.Throw<InvalidDataException>(
            () => ShaderFileReader.Read(new MemoryStream(bytes)));
        var partial = Should.Throw<InvalidDataException>(
            () => ShaderFileReader.ReadPipeline(new MemoryStream(bytes), GraphicsBackend.OpenGL));

        foreach (InvalidDataException error in new[] { whole, partial })
        {
            error.Message.ShouldContain(bogus.ToString());
            error.Message.ShouldContain(EngineInfo.ShaderFormatVersion.ToString());
            error.Message.ShouldContain("recook", Case.Insensitive);
        }
    }

    [Fact]
    public void A_file_this_engine_wrote_declares_this_engines_format_version()
    {
        byte[] bytes = WriteToBytes(File(Blob(GraphicsBackend.OpenGL, instanced: true)));

        ShaderFileReader.Read(new MemoryStream(bytes))
            .FormatVersion.ShouldBe(EngineInfo.ShaderFormatVersion);
    }

    [Fact]
    public void ReadPipeline_and_Read_agree_on_the_data_section_start()
    {
        // Three pipelines: a wrong data-section origin only moves a blob past the first.
        CompiledShaderFile file = File(
            Blob(GraphicsBackend.OpenGL, instanced: false),
            Blob(GraphicsBackend.D3D11, instanced: true),
            Blob(GraphicsBackend.D3D12, instanced: true));

        byte[] bytes = WriteToBytes(file);

        PipelineBlob whole = ShaderFileReader.Read(new MemoryStream(bytes))
            .GetPipeline(GraphicsBackend.D3D12).ShouldNotBeNull();
        PipelineBlob partial = ShaderFileReader
            .ReadPipeline(new MemoryStream(bytes), GraphicsBackend.D3D12)
            .ShouldNotBeNull();

        partial.VertexData.ShouldBe(whole.VertexData);
        partial.FragmentData.ShouldBe(whole.FragmentData);
        partial.InstancedVertexData.ShouldBe(whole.InstancedVertexData);
        partial.VertexInputs.ShouldBe(whole.VertexInputs);
        partial.InstancedVertexInputs.ShouldBe(whole.InstancedVertexInputs);

        // Rules out both paths reading the first blob from the same wrong origin.
        partial.VertexData.ShouldBe(StageData(GraphicsBackend.D3D12, "vertex"));
    }

    [Fact]
    public void The_span_reader_and_the_stream_reader_agree_byte_for_byte()
    {
        // Three pipelines, so a blob at a non-zero offset is compared too.
        CompiledShaderFile file = File(
            Blob(GraphicsBackend.OpenGL, instanced: false),
            Blob(GraphicsBackend.D3D11, instanced: true),
            Blob(GraphicsBackend.D3D12, instanced: true));

        byte[] bytes = WriteToBytes(file);

        foreach (GraphicsBackend backend in new[]
                 { GraphicsBackend.OpenGL, GraphicsBackend.D3D11, GraphicsBackend.D3D12 })
        {
            PipelineBlob stream = ShaderFileReader
                .ReadPipeline(new MemoryStream(bytes), backend).ShouldNotBeNull();
            PipelineBlob span = ShaderFileReader
                .ReadPipeline(bytes.AsSpan(), backend).ShouldNotBeNull();

            // If the two parsers diverge, a stage is read from the wrong bytes
            // and nothing throws.
            span.Backend.ShouldBe(stream.Backend);
            span.Format.ShouldBe(stream.Format);
            span.Stages.ShouldBe(stream.Stages);
            span.VertexData.ShouldBe(stream.VertexData);
            span.FragmentData.ShouldBe(stream.FragmentData);
            span.GeometryData.ShouldBe(stream.GeometryData);
            span.ComputeData.ShouldBe(stream.ComputeData);
            span.VertexInputs.ShouldBe(stream.VertexInputs);
            span.InstancedVertexData.ShouldBe(stream.InstancedVertexData);
            span.InstancedVertexInputs.ShouldBe(stream.InstancedVertexInputs);
        }
    }

    [Fact]
    public void The_span_reader_answers_null_for_a_backend_the_file_does_not_carry()
    {
        // Null, not a throw: the engine falls back to compiling from source.
        byte[] bytes = WriteToBytes(File(Blob(GraphicsBackend.D3D11, instanced: true)));

        ShaderFileReader.ReadPipeline(bytes.AsSpan(), GraphicsBackend.OpenGL).ShouldBeNull();
        ShaderFileReader.ReadPipeline(new MemoryStream(bytes), GraphicsBackend.OpenGL).ShouldBeNull();
    }

    [Fact]
    public void The_backend_listing_reads_the_table_and_nothing_else()
    {
        CompiledShaderFile file = File(
            Blob(GraphicsBackend.D3D11, instanced: true),
            Blob(GraphicsBackend.OpenGL, instanced: false));

        // Table order, not sorted, so verify output does not depend on enum values.
        ShaderFileReader.ReadBackends(WriteToBytes(file))
            .ShouldBe([GraphicsBackend.D3D11, GraphicsBackend.OpenGL]);
    }

    [Fact]
    public void A_truncated_file_is_refused_rather_than_read_short()
    {
        byte[] bytes = WriteToBytes(File(Blob(GraphicsBackend.OpenGL, instanced: true)));

        // Half a file. BinaryReader.ReadBytes would return a short array instead.
        Should.Throw<InvalidDataException>(
            () => ShaderFileReader.ReadPipeline(bytes.AsSpan(0, bytes.Length / 2), GraphicsBackend.OpenGL));
    }

    private static byte[] WriteToBytes(CompiledShaderFile file)
    {
        using var stream = new MemoryStream();
        ShaderFileWriter.Write(stream, file);
        return stream.ToArray();
    }

    private static CompiledShaderFile File(params PipelineBlob[] pipelines) => new()
    {
        FormatVersion = EngineInfo.ShaderFormatVersion,
        Stages = ShaderStageFlags.Vertex | ShaderStageFlags.Fragment,
        Pipelines = pipelines,
    };

    // Lengths differ per backend, so a blob read at the wrong offset can't match.
    private static byte[] StageData(GraphicsBackend backend, string stage) =>
        Encoding.UTF8.GetBytes($"{backend}:{stage}:{new string('x', (int)backend * 7)}");

    private static PipelineBlob Blob(GraphicsBackend backend, bool instanced) => new()
    {
        Backend = backend,
        Format = ShaderDataFormat.SourceText,
        Stages = ShaderStageFlags.Vertex | ShaderStageFlags.Fragment,
        VertexData = StageData(backend, "vertex"),
        FragmentData = StageData(backend, "fragment"),
        VertexInputs =
        [
            new VertexInputElement("position", 0, 1, 3, VertexInputRate.PerVertex),
            new VertexInputElement("normal", 1, 1, 3, VertexInputRate.PerVertex),
            new VertexInputElement("uv", 2, 1, 2, VertexInputRate.PerVertex),
        ],
        InstancedVertexData = instanced ? StageData(backend, "instanced") : null,
        InstancedVertexInputs = instanced
            ?
            [
                new VertexInputElement("position", 0, 1, 3, VertexInputRate.PerVertex),
                new VertexInputElement("normal", 1, 1, 3, VertexInputRate.PerVertex),
                new VertexInputElement("uv", 2, 1, 2, VertexInputRate.PerVertex),
                new VertexInputElement("uModel", 3, 4, 4, VertexInputRate.PerInstance),
            ]
            : [],
    };

    // Written by hand, not through ShaderFileWriter, so a header change is caught.
    private static byte[] HeaderOnly(ushort formatVersion) =>
    [
        (byte)'S', (byte)'S', (byte)'C', (byte)'O',
        (byte)(formatVersion & 0xFF), (byte)(formatVersion >> 8),
        (byte)(ShaderStageFlags.Vertex | ShaderStageFlags.Fragment),
        0,
    ];
}
