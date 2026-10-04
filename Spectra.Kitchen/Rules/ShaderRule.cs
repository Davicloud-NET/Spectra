using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Compiles a <c>.spectrashade</c> source file into a <c>.specshadecomp</c> with
/// one blob per requested backend.
/// </summary>
// Compiler diagnostics all report under SC6001 with the message verbatim: the
// compiler has no diagnostic numbers of its own to wrap.
public sealed class ShaderRule : IRule
{
    /// <summary>Source extension, dot included.</summary>
    public const string SourceExtension = BaseShaders.SourceExtension;

    /// <summary>Cooked extension, dot included.</summary>
    public const string CookedExtension = CompiledShaderFile.FileExtension;

    /// <inheritdoc/>
    public RuleKind Kind => RuleKind.Shader;

    /// <inheritdoc/>
    // A codegen change that leaves the container alone still needs this or the
    // shaderFormat tool version in the cache key raised by hand.
    public int Version => 1;

    /// <inheritdoc/>
    public CookSettingKeys SettingsRead => CookSettingKeys.Targets;

    /// <summary>
    /// The content path the cooked form of <paramref name="sourcePath"/> is emitted
    /// at. Must agree with <c>BaseShaders.CookedContentPath</c>.
    /// </summary>
    public static string CookedPathFor(string sourcePath) =>
        Path.ChangeExtension(sourcePath, CookedExtension);

    /// <summary>Whether <paramref name="contentPath"/> is a shader source file.</summary>
    public static bool Handles(string contentPath) =>
        contentPath.EndsWith(SourceExtension, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public void Cook(IRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string source = DecodeUtf8(context.Read(context.SourcePath));

        // A repeated target would write a duplicate pipeline entry.
        GraphicsBackend[] targets = DistinctTargets(context.Targets);
        if (targets.Length == 0)
        {
            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.ShaderNoTargets,
                $"'{context.SourcePath}' was cooked with no target backends, so it produced no blob and is " +
                "not in this pack. Name at least one --target.",
                context.SourcePath));

            return;
        }

        CompiledShaderFile compiled;
        try
        {
            compiled = new SpectraShadeCompiler().Compile(source, targets);
        }
        catch (ShaderCompilationException ex)
        {
            foreach (Diagnostic diagnostic in ex.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Info) continue;

                context.Report(Translate(diagnostic, context.SourcePath));
            }

            // ex.Message is a join of the same diagnostics, so it is not reported too.
            return;
        }
        catch (NotImplementedException ex)
        {
            // SpirVGenerator: a target with no code generator yet.
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.ShaderBackendUnsupported,
                $"'{context.SourcePath}' could not be cooked for every requested target: {ex.Message}",
                context.SourcePath));

            return;
        }

        CompiledShaderFile filtered = KeepOnly(compiled, targets, context);
        if (filtered.Pipelines.Count == 0) return;

        using var bytes = new MemoryStream();
        ShaderFileWriter.Write(bytes, filtered);

        context.Emit(CookedPathFor(context.SourcePath), bytes.ToArray(), PackEntryKind.Shader);
    }

    // Filters on the writer side as well, so a pack never carries a blob for a
    // backend it was not cooked for whatever the compiler returns. A requested
    // backend with no blob is an error: the engine would compile it at runtime.
    private static CompiledShaderFile KeepOnly(
        CompiledShaderFile compiled, GraphicsBackend[] targets, IRuleContext context)
    {
        var kept = new List<PipelineBlob>(targets.Length);
        var stages = ShaderStageFlags.None;

        // Target order, not the compiler's, so the output is deterministic.
        for (int i = 0; i < targets.Length; i++)
        {
            PipelineBlob? blob = compiled.GetPipeline(targets[i]);
            if (blob is null)
            {
                context.Report(CookDiagnostic.Error(
                    CookDiagnosticCodes.ShaderBackendMissing,
                    $"'{context.SourcePath}' was cooked for {CookSettingsDigest.ToWire(targets[i])} and the " +
                    "compiler produced no pipeline for it.",
                    context.SourcePath));

                continue;
            }

            kept.Add(blob);
            stages |= blob.Stages;
        }

        return new CompiledShaderFile
        {
            FormatVersion = compiled.FormatVersion,
            Stages = stages,
            Pipelines = kept,
        };
    }

    private static GraphicsBackend[] DistinctTargets(IReadOnlyList<GraphicsBackend> targets)
    {
        var distinct = new List<GraphicsBackend>(targets.Count);
        for (int i = 0; i < targets.Count; i++)
        {
            if (!distinct.Contains(targets[i])) distinct.Add(targets[i]);
        }

        return [.. distinct];
    }

    private static CookDiagnostic Translate(Diagnostic diagnostic, string sourcePath)
    {
        // One-based on both sides. A span with no position reports against the whole file.
        int line = diagnostic.Span.Start.Line;
        int column = diagnostic.Span.Start.Column;

        return diagnostic.Severity == DiagnosticSeverity.Error
            ? CookDiagnostic.Error(CookDiagnosticCodes.ShaderCompileFailed, diagnostic.Message, sourcePath, line, column)
            : CookDiagnostic.Warning(CookDiagnosticCodes.ShaderCompileFailed, diagnostic.Message, sourcePath, line, column);
    }

    // Strips a BOM, which the lexer would report as a syntax error on line one.
    private static string DecodeUtf8(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        if (span.Length >= 3 && span[..3].SequenceEqual(bom)) span = span[3..];

        return Encoding.UTF8.GetString(span);
    }
}
