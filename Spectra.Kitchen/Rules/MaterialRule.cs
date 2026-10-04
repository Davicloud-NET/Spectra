using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Images;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Graphics.Shaders;
using System;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Validates a <c>.spectramat</c> and packs it as source text, verbatim.
/// </summary>
// Text, not a binary form: the parser tolerates unknown keys, and a binary
// shape would have to drop them. Severity of what it reports is CookGate's call.
public sealed class MaterialRule : IRule
{
    /// <inheritdoc/>
    public RuleKind Kind => RuleKind.Material;

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public CookSettingKeys SettingsRead => CookSettingKeys.None;

    /// <summary>Whether <paramref name="contentPath"/> is a material file.</summary>
    public static bool Handles(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);
        return contentPath.EndsWith(MaterialParser.FileExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public void Cook(IRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        byte[] source = context.Read(context.SourcePath);
        MaterialDefinition material = MaterialParser.ParseUtf8(source, context.SourcePath);

        foreach (string warning in material.Warnings)
        {
            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.MaterialFileMalformed, warning, context.SourcePath));
        }

        var content = new RuleContentSource(context);

        foreach (MaterialTextureSlot slot in material.Textures)
        {
            string resolved = ImageContentPath.Resolve(content, slot.TexturePath);
            if (content.Exists(resolved)) continue;

            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.MaterialTextureMissing,
                $"'{context.SourcePath}' binds sampler '{slot.Name}' to '{slot.TexturePath}', which is not in " +
                "the content root. The running engine would show the magenta placeholder and carry on; a " +
                "shipped build would ship that.",
                context.SourcePath));
        }

        ReportUnresolvableShader(context, content, material.ShaderName);

        // Emits even after an error. The diagnostic already fails the cook, and
        // holding the output back would make this material look missing downstream.
        context.Emit(context.SourcePath, source, PackEntryKind.Material);
    }

    // Only checks content. A shader that a host's resolver callback provides at
    // runtime is invisible here and gets reported.
    private static void ReportUnresolvableShader(
        IRuleContext context, RuleContentSource content, string? shaderName)
    {
        // No shader key means the built-in.
        if (string.IsNullOrEmpty(shaderName)) return;
        if (IsBuiltIn(shaderName)) return;

        // A path as written, or a name under the Shaders folder, source or cooked.
        if (content.Exists(shaderName)) return;
        if (content.Exists($"{BaseShaders.ContentFolder}/{shaderName}{ShaderRule.SourceExtension}")) return;
        if (content.Exists($"{BaseShaders.ContentFolder}/{shaderName}{ShaderRule.CookedExtension}")) return;

        context.Report(CookDiagnostic.Error(
            CookDiagnosticCodes.MaterialShaderMissing,
            $"'{context.SourcePath}' names shader '{shaderName}', which is neither the built-in " +
            $"'{MaterialParser.BuiltInShaderName}' nor a shader in this project. The running engine would draw " +
            "with the built-in lit program instead and log a warning; a shipped build would render that.",
            context.SourcePath));
    }

    // BaseShaders ship embedded in the engine assembly, so none of them is ever missing.
    private static bool IsBuiltIn(string shaderName)
    {
        if (string.Equals(shaderName, MaterialParser.BuiltInShaderName, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (string fileName in BaseShaders.FileNames)
        {
            ReadOnlySpan<char> stem = fileName.AsSpan(0, fileName.Length - BaseShaders.SourceExtension.Length);
            if (stem.Equals(shaderName, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
