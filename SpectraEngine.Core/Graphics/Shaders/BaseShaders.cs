using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SpectraEngine.Core.Graphics.Shaders;

/// <summary>
/// The engine's built-in SpectraShade sources. They are embedded in the
/// assembly, and in a developer build the files on disk can be located for
/// hot-reload.
/// </summary>
public static class BaseShaders
{
    /// <summary>The extension of a SpectraShade source file.</summary>
    public const string SourceExtension = ".spectrashade";

    /// <summary>
    /// The content folder a built-in shader resolves under, whether it comes
    /// from a folder, a pack or the embedded copy.
    /// </summary>
    public const string ContentFolder = "Shaders";

    /// <summary>File name of the built-in lit shader.</summary>
    public const string LitFileName = "Lit.spectrashade";

    /// <summary>File name of the debug-line shader.</summary>
    public const string DebugLineFileName = "DebugLine.spectrashade";

    /// <summary>File name of the tone-mapping resolve shader.</summary>
    public const string PostResolveFileName = "PostResolve.spectrashade";

    /// <summary>File name of the deferred geometry pass.</summary>
    public const string GBufferFillFileName = "GBufferFill.spectrashade";
    public const string GBufferFillCompactFileName = "GBufferFillCompact.spectrashade";

    /// <summary>File name of the deferred light pass.</summary>
    public const string DeferredLightFileName = "DeferredLight.spectrashade";

    /// <summary>File name of the shadow depth pass.</summary>
    public const string ShadowDepthFileName = "ShadowDepth.spectrashade";

    /// <summary>File name of the depth-tested world line shader.</summary>
    public const string WorldLineFileName = "WorldLine.spectrashade";

    /// <summary>File name of the world line's deferred, blended half.</summary>
    public const string WorldLineBlendFileName = "WorldLineBlend.spectrashade";

    // MSBuild's resource name for Graphics\BaseShaders\<file>. Exact name, not
    // a suffix match, which an unrelated file could satisfy.
    private const string ResourcePrefix = "SpectraEngine.Core.Graphics.BaseShaders.";

    // A test checks this list against the embedded resources.
    private static readonly string[] AllFileNames =
    [
        LitFileName,
        DebugLineFileName,
        PostResolveFileName,
        GBufferFillFileName,
        GBufferFillCompactFileName,
        DeferredLightFileName,
        ShadowDepthFileName,
        WorldLineFileName,
        WorldLineBlendFileName,
    ];

    private static int _hotReloadStateLogged;

    /// <summary>File names of every built-in shader.</summary>
    public static IReadOnlyList<string> FileNames => AllFileNames;

    /// <summary>The built-in forward lit shader.</summary>
    public static string Lit => ReadEmbedded(LitFileName);

    /// <summary>The unlit vertex-coloured shader debug draw uses.</summary>
    public static string DebugLine => ReadEmbedded(DebugLineFileName);

    /// <summary>The tone-mapping resolve.</summary>
    public static string PostResolve => ReadEmbedded(PostResolveFileName);

    /// <summary>The deferred geometry pass.</summary>
    public static string GBufferFill => ReadEmbedded(GBufferFillFileName);
    public static string GBufferFillCompact => ReadEmbedded(GBufferFillCompactFileName);

    /// <summary>The deferred light pass.</summary>
    public static string DeferredLight => ReadEmbedded(DeferredLightFileName);

    /// <summary>The shadow map's depth pass.</summary>
    public static string ShadowDepth => ReadEmbedded(ShadowDepthFileName);

    /// <summary>The depth-tested world line, single target.</summary>
    public static string WorldLine => ReadEmbedded(WorldLineFileName);

    /// <summary>The world line's deferred half: blended after the light pass, depth tested in the shader.</summary>
    public static string WorldLineBlend => ReadEmbedded(WorldLineBlendFileName);


    /// <summary>
    /// The content-relative path of <paramref name="fileName"/>, e.g.
    /// <c>Shaders/Lit.spectrashade</c>.
    /// </summary>
    public static string ContentPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return $"{ContentFolder}/{fileName}";
    }

    /// <summary>
    /// The content-relative path of the cooked form of <paramref name="fileName"/>,
    /// e.g. <c>Shaders/Lit.specshadecomp</c>.
    /// </summary>
    // Must match what the shader cook rule emits. A mismatch is no error: the
    // lookup misses and the engine compiles from source.
    public static string CookedContentPath(string fileName) =>
        ContentPath(Path.ChangeExtension(fileName, CompiledShaderFile.FileExtension));

    /// <summary>
    /// The absolute path of <paramref name="fileName"/> in the source tree, or
    /// null when the engine is not running from a developer build.
    /// </summary>
    public static string? TryResolveSourcePath(string fileName)
    {
        string? root = TryFindSourceRoot();
        if (root is null) return null;
        string candidate = Path.Combine(root, "SpectraEngine.Core", "Graphics", "BaseShaders", fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>Source-file path for <see cref="Lit"/>, if locatable on disk.</summary>
    public static string? LitPath => TryResolveSourcePath(LitFileName);

    /// <summary>Source-file path for <see cref="DebugLine"/>, if locatable on disk.</summary>
    public static string? DebugLinePath => TryResolveSourcePath(DebugLineFileName);

    /// <summary>Source-file path for <see cref="PostResolve"/>, if locatable on disk.</summary>
    public static string? PostResolvePath => TryResolveSourcePath(PostResolveFileName);

    /// <summary>Source-file path for <see cref="GBufferFill"/>, if locatable on disk.</summary>
    public static string? GBufferFillPath => TryResolveSourcePath(GBufferFillFileName);

    /// <summary>Source-file path for <see cref="DeferredLight"/>, if locatable on disk.</summary>
    public static string? DeferredLightPath => TryResolveSourcePath(DeferredLightFileName);

    /// <summary>Source-file path for <see cref="ShadowDepth"/>, if locatable on disk.</summary>
    public static string? ShadowDepthPath => TryResolveSourcePath(ShadowDepthFileName);

    /// <summary>Source-file path for <see cref="WorldLine"/>, if locatable on disk.</summary>
    public static string? WorldLinePath => TryResolveSourcePath(WorldLineFileName);

    /// <summary>Source-file path for <see cref="WorldLineBlend"/>, if locatable on disk.</summary>
    public static string? WorldLineBlendPath => TryResolveSourcePath(WorldLineBlendFileName);

    /// <summary>
    /// Opens the embedded source for <paramref name="fileName"/> (a bare file
    /// name, e.g. <c>Lit.spectrashade</c>). The caller owns the stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No resource is embedded under that name.
    /// </exception>
    public static Stream OpenEmbedded(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var assembly = typeof(BaseShaders).Assembly;
        string resourceName = ResourcePrefix + fileName;
        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded shader resource '{fileName}' (looked up as '{resourceName}') " +
                $"not found in {assembly.GetName().Name}.");
    }

    /// <summary>
    /// The embedded source text for <paramref name="fileName"/> (a bare file
    /// name). Every other shader lookup falls back to this.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No resource is embedded under that name.
    /// </exception>
    public static string ReadEmbeddedSource(string fileName) => ReadEmbedded(fileName);

    private static string ReadEmbedded(string fileName)
    {
        using Stream stream = OpenEmbedded(fileName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Logs once whether shader hot-reload is live, and why not when it is off.
    /// Later calls do nothing.
    /// </summary>
    public static void LogHotReloadState(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (Interlocked.Exchange(ref _hotReloadStateLogged, 1) != 0) return;

        string? root = TryFindSourceRoot();
        if (root is null)
        {
            logger.LogWarning(
                "Shader hot-reload off: no .slnx or .sln above the base directory {BaseDirectory}, " +
                "so the SpectraShade sources cannot be located on disk. Shaders come from the " +
                "embedded copies and edits to them will not be picked up (expected in a deployed " +
                "or NativeAOT-published build).",
                AppContext.BaseDirectory);
            return;
        }

        string directory = Path.Combine(root, "SpectraEngine.Core", "Graphics", "BaseShaders");
        if (!Directory.Exists(directory))
        {
            logger.LogWarning(
                "Shader hot-reload off: the source tree at {Root} has no {Directory}, " +
                "so shaders come from the embedded copies and edits will not be picked up.",
                root, directory);
            return;
        }

        logger.LogInformation("Shader hot-reload on; sources under {Directory}", directory);
    }

    // A solution file above the executable marks a developer build.
    private static string? TryFindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
