using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.IO;
using System.Text;

namespace SpectraEngine.Core.Graphics.Shaders;

/// <summary>
/// Where one shader came from: a cooked blob, or source text to compile.
/// One of <paramref name="Cooked"/> and <paramref name="Source"/> is non-null.
/// </summary>
/// <param name="WatchPath">
/// Path to watch for hot-reload, or null when no file on disk backs the shader.
/// </param>
public readonly record struct ResolvedShader(PipelineBlob? Cooked, string? Source, string? WatchPath);

/// <summary>
/// Resolves a built-in shader through the content stack: cooked blob first,
/// then source, then the copy embedded in this assembly.
/// </summary>
// No Exists probe before TryOpen: the two can disagree across sources.
// A cooked file with no blob for this backend is logged and falls through to
// source, so a mis-targeted pack still renders.
public static class BaseShaderResolver
{
    /// <summary>
    /// Resolves the built-in shader <paramref name="fileName"/> (a bare file
    /// name, e.g. <c>Lit.spectrashade</c>) for <paramref name="backend"/>.
    /// A null <paramref name="content"/> resolves to the embedded copy.
    /// </summary>
    public static ResolvedShader ResolveBuiltIn(
        IContentSource? content, string fileName, GraphicsBackend backend, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(logger);

        if (content is not null)
        {
            string cookedPath = BaseShaders.CookedContentPath(fileName);
            if (TryReadCooked(content, cookedPath, backend, logger) is { } cooked)
                return new ResolvedShader(cooked, Source: null, WatchPath: null);

            string sourcePath = BaseShaders.ContentPath(fileName);
            if (content.TryOpen(sourcePath, out ContentBlob? blob))
            {
                using (blob)
                {
                    string text = DecodeUtf8(blob.Span);

                    // A packed source has no watch path and is not watched.
                    content.TryGetWatchPath(sourcePath, out string? watch);
                    return new ResolvedShader(Cooked: null, text, watch);
                }
            }
        }

        // Prefer the source tree's file over the embedded copy. The resource is
        // a build-time snapshot, so serving it would hide edits the watcher on
        // this path reports.
        string? diskPath = BaseShaders.TryResolveSourcePath(fileName);
        if (diskPath is not null)
        {
            try
            {
                return new ResolvedShader(Cooked: null, File.ReadAllText(diskPath), diskPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File moved between the existence check and the read.
                logger.LogWarning(
                    "Shader source '{Path}' could not be read; using the embedded copy: {Reason}",
                    diskPath, ex.Message);
            }
        }

        return new ResolvedShader(Cooked: null, BaseShaders.ReadEmbeddedSource(fileName), WatchPath: null);
    }

    private static PipelineBlob? TryReadCooked(
        IContentSource content, string cookedPath, GraphicsBackend backend, ILogger logger)
    {
        if (!content.TryOpen(cookedPath, out ContentBlob? blob)) return null;

        using (blob)
        {
            PipelineBlob? pipeline;
            try
            {
                // Span overload: no copy of a mapped pack view.
                pipeline = ShaderFileReader.ReadPipeline(blob.Span, backend);
            }
            catch (InvalidDataException ex)
            {
                // Don't throw: the caller falls back to source.
                logger.LogError(
                    "Compiled shader '{Path}' could not be read and was ignored: {Reason}",
                    cookedPath, ex.Message);

                return null;
            }

            if (pipeline is not null) return pipeline;

            logger.LogError(
                "Compiled shader '{Path}' carries no blob for {Backend}, so it was compiled from source " +
                "instead. The pack was cooked for a different target list than this run is using.",
                cookedPath, backend);

            return null;
        }
    }

    // Strips a BOM, which the lexer would report as a syntax error on line one.
    private static string DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        if (bytes.Length >= 3 && bytes[..3].SequenceEqual(bom)) bytes = bytes[3..];

        return Encoding.UTF8.GetString(bytes);
    }
}
