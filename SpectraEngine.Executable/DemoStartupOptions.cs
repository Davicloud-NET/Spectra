using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

// For the argument-parsing tests.
[assembly: InternalsVisibleTo("SpectraEngine.Editing.Tests")]

namespace SpectraEngine.Executable;

internal enum SelfTestSource
{
    Default,
    CommandLine,
    Environment,
}

// The demo host's command line. The self-test is off unless asked for: it
// drags a real brush every few seconds, which is wrong for an interactive run.
internal sealed record DemoStartupOptions(
    GraphicsBackend Backend,
    bool SelfTestEnabled,
    SelfTestSource SelfTestSource,
    TimeSpan? FullscreenCycleInterval = null,
    bool StartInPlayMode = false,
    bool OffscreenProbe = false,
    string? Pipeline = null,
    bool Shadows = true,
    bool Profile = false,
    bool VSync = false,
    bool? DebugLayer = null,
    string? Adapter = null,
    (int Width, int Height)? WindowSize = null,
    int? ScatterGrid = null,
    int? PropCount = null,
    string? LoadMapPath = null,
    string? SaveMapPath = null,
    string? ProjectPath = null,
    string? SaveProjectPath = null,
    bool BootFromPacks = false,
    bool DevContentOverlay = false,
    string? ExportEntitySchemaPath = null,
    bool ExitAfterSave = false,
    bool ViewportCompare = false,
    bool PacingProbe = false,
    bool DemoCsgAnimation = false,
    int FrameContexts = 2,
    bool Uncapped = false,
    GBufferLayout GBufferLayout = GBufferLayout.Standard,
    bool PipelineCompare = false)
{
    // Console lines to run once the scene is loaded, in the order given.
    public IReadOnlyList<string> Commands { get; init; } = [];

    // Read only when no command-line switch names the self-test.
    public const string SelfTestEnvironmentVariable = "SPECTRA_SELFTEST";

    private const string Usage =
        "Usage: SpectraEngine.Executable [opengl|d3d11|d3d12] [--selftest[=true|false]] " +
        "[--fullscreen-cycle[=seconds]] [--play[=true|false]] [--offscreen-probe[=true|false]] " +
        "[--pipeline=<name>] [--shadows[=true|false]] [--profile[=true|false]] " +
        "[--vsync[=true|false]] [--demo-animation=off|csg] [--frame-contexts=1|2|3] [--uncapped] [--gbuffer=standard|extended] " +
        "[--debug-layer[=true|false]] [--adapter=<name>] [--size=WxH] [--parts=<grid>] " +
        "[--props=<count>] [--map=<bundle.smap>] [--save-map=<bundle.smap>] " +
        "[--project=<folder>] [--save-project=<folder>] [--pack[=true|false]] [--dev[=true|false]] " +
        "[--exit-after-save[=true|false]] " +
        "[--export-entity-schema=<file.sentdef>] [--viewport-compare[=true|false]] " +
        "[--pacing-probe[=true|false]] [--pipeline-compare[=true|false]] [--command=<console line>].";

    // Throws ArgumentException on a bad argument; Program logs it as a usage error.
    // An explicit --selftest=false beats the environment value.
    public static DemoStartupOptions Parse(IReadOnlyList<string> args, string? selfTestEnvironmentValue)
    {
        ArgumentNullException.ThrowIfNull(args);

        GraphicsBackend? backend = null;
        bool? selfTest = null;
        bool play = false;
        bool offscreenProbe = false;
        string? pipeline = null;
        bool shadows = true;
        bool profile = false;
        bool vsync = false;
        bool demoCsgAnimation = false;
        int frameContexts = 2;
        bool uncapped = false;
        GBufferLayout gbufferLayout = GBufferLayout.Standard;
        bool? debugLayer = null;
        string? adapter = null;
        (int, int)? windowSize = null;
        int? scatterGrid = null;
        int? propCount = null;
        string? loadMapPath = null;
        string? saveMapPath = null;
        string? projectPath = null;
        string? saveProjectPath = null;
        bool bootFromPacks = false;
        bool devContentOverlay = false;
        string? exportEntitySchemaPath = null;
        bool exitAfterSave = false;
        bool viewportCompare = false;
        bool pacingProbe = false;
        bool pipelineCompare = false;
        TimeSpan? fullscreenCycle = null;
        IReadOnlyList<string> commands = [];

        for (int i = 0; i < args.Count; i++)
        {
            string raw = args[i];
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            // Leading dashes and slashes are optional; every switch takes name=value.
            string token = raw.Trim();
            string body = token.TrimStart('-', '/');
            int equals = body.IndexOf('=');
            string name = (equals < 0 ? body : body[..equals]).ToLowerInvariant();
            string? value = equals < 0 ? null : body[(equals + 1)..];

            switch (name)
            {
                case "selftest" or "self-test":
                    selfTest = ParseBoolean(value, token);
                    continue;

                case "backend":
                    backend = ParseBackend(value ?? string.Empty, token);
                    continue;

                case "fullscreen-cycle" or "fullscreencycle":
                    fullscreenCycle = ParseInterval(value, token);
                    continue;

                case "play":
                    play = ParseBoolean(value, token);
                    continue;

                // Draws startup frames into an offscreen target too. The D3D
                // backends have no headless fixture, so this is their coverage.
                case "offscreen-probe" or "offscreenprobe":
                    offscreenProbe = ParseBoolean(value, token);
                    continue;

                // Not validated here: the pipeline set is the renderer's and
                // differs per backend.
                case "pipeline":
                    pipeline = ParseName(value, token);
                    continue;

                case "shadows":
                    shadows = ParseBoolean(value, token);
                    continue;

                case "profile":
                    profile = ParseBoolean(value, token);
                    continue;

                // Off by default: a frame time under vsync measures the monitor.
                case "vsync" or "v-sync":
                    vsync = ParseBoolean(value, token);
                    continue;
                case "demo-animation":
                    demoCsgAnimation = value?.ToLowerInvariant() switch
                    {
                        "off" => false,
                        "csg" => true,
                        _ => throw new ArgumentException("--demo-animation requires off or csg. " + Usage),
                    };
                    continue;
                case "frame-contexts":
                    frameContexts = ParseCount(value, token);
                    if (frameContexts is < 1 or > 3)
                        throw new ArgumentException("--frame-contexts requires 1, 2 or 3. " + Usage);
                    continue;
                case "uncapped":
                    uncapped = ParseBoolean(value, token);
                    continue;
                case "gbuffer":
                    gbufferLayout = value?.ToLowerInvariant() switch
                    {
                        "standard" => GBufferLayout.Standard,
                        "extended" => GBufferLayout.Extended,
                        _ => throw new ArgumentException("--gbuffer requires standard or extended. " + Usage),
                    };
                    continue;

                // Overrides the build default (on in Debug, off in Release).
                case "debug-layer" or "debuglayer":
                    debugLayer = ParseBoolean(value, token);
                    continue;

                // Substring of the adapter name.
                case "adapter" or "gpu":
                    adapter = ParseName(value, token);
                    continue;

                case "size" or "resolution":
                    windowSize = ParseSize(value, token);
                    continue;

                // Side length of the scattered-brush grid.
                case "parts" or "scatter":
                    scatterGrid = ParseCount(value, token);
                    continue;

                // A count, not a grid side.
                case "props":
                    propCount = ParseCount(value, token);
                    continue;

                // A .smap bundle is a directory.
                case "map" or "load-map" or "loadmap":
                    loadMapPath = ParseName(value, token);
                    continue;

                case "save-map" or "savemap":
                    saveMapPath = ParseName(value, token);
                    continue;

                // The .spectraproj file or the folder containing it.
                case "project":
                    projectPath = ParseName(value, token);
                    continue;

                case "save-project" or "saveproject":
                    saveProjectPath = ParseName(value, token);
                    continue;

                // Boot from the cooked packs, with no loose files to fall back on.
                case "pack" or "packs":
                    bootFromPacks = ParseBoolean(value, token);
                    continue;

                // Loose files over the packs. Only valid with --pack.
                case "dev":
                    devContentOverlay = ParseBoolean(value, token);
                    continue;

                // The run lasts one real frame: a scene can't be saved before
                // the render thread has created its meshes and textures.
                case "exit-after-save" or "exitaftersave":
                    exitAfterSave = ParseBoolean(value, token);
                    continue;

                // Writes the .sentdef and exits without opening a window.
                case "export-entity-schema" or "exportentityschema":
                    exportEntitySchemaPath = ParseName(value, token);
                    continue;

                // Runs on a composited surface with no window, then exits.
                case "viewport-compare" or "viewportcompare":
                    viewportCompare = ParseBoolean(value, token);
                    continue;

                // Same: no window, prints its table and exits.
                case "pacing-probe" or "pacingprobe":
                    pacingProbe = ParseBoolean(value, token);
                    continue;

                // Draws the scene with the deferred pipeline and the forward
                // one, compares the two pictures and exits.
                case "pipeline-compare" or "pipelinecompare":
                    pipelineCompare = ParseBoolean(value, token);
                    continue;

                // Repeatable. Kept as typed: the console does its own splitting.
                case "command":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new ArgumentException(
                            $"'{token}' needs a console line, e.g. --command=\"ent_list\". {Usage}");
                    }

                    commands = [.. commands, value];
                    continue;
            }

            // Anything else is the positional backend. A second one is a typo.
            if (backend is not null)
                throw new ArgumentException($"Unexpected argument '{token}'. {Usage}");

            backend = ParseBackend(body, token);
        }

        // Alone it would end the run one frame in with nothing written.
        if (exitAfterSave && saveMapPath is null && saveProjectPath is null)
        {
            throw new ArgumentException(
                $"'--exit-after-save' needs something to save: name --save-map or --save-project. {Usage}");
        }

        // Without a project, --pack would run the demo scene off loose files
        // and look like a passing cooked run.
        if (bootFromPacks && projectPath is null)
        {
            throw new ArgumentException(
                $"'--pack' needs a project to take its pack list from: name --project. {Usage}");
        }

        if (devContentOverlay && !bootFromPacks)
        {
            throw new ArgumentException(
                $"'--dev' only means something over a pack mount: name --pack too. {Usage}");
        }

        // OpenGL has no shared render target, and a composited surface has no
        // GL context. Refuse here so it doesn't surface as a driver failure.
        if (viewportCompare && (backend ?? GraphicsBackend.OpenGL) == GraphicsBackend.OpenGL)
        {
            throw new ArgumentException(
                "'--viewport-compare' needs a backend that can share a render target: name d3d11 or d3d12. " +
                Usage);
        }

        if (pacingProbe && (backend ?? GraphicsBackend.OpenGL) == GraphicsBackend.OpenGL)
        {
            throw new ArgumentException(
                "'--pacing-probe' needs a backend that can share a render target: name d3d11 or d3d12. " +
                Usage);
        }

        if (selfTest is bool fromCommandLine)
            return new DemoStartupOptions(
                backend ?? GraphicsBackend.OpenGL, fromCommandLine, SelfTestSource.CommandLine,
                fullscreenCycle, play, offscreenProbe, pipeline, shadows, profile, vsync, debugLayer, adapter, windowSize, scatterGrid, propCount,
                loadMapPath, saveMapPath, projectPath, saveProjectPath, bootFromPacks, devContentOverlay,
                exportEntitySchemaPath, exitAfterSave, viewportCompare, pacingProbe, demoCsgAnimation, frameContexts, uncapped, gbufferLayout, pipelineCompare) { Commands = commands };

        if (!string.IsNullOrWhiteSpace(selfTestEnvironmentValue))
        {
            bool fromEnvironment = ParseBoolean(
                selfTestEnvironmentValue.Trim(), SelfTestEnvironmentVariable);
            return new DemoStartupOptions(
                backend ?? GraphicsBackend.OpenGL, fromEnvironment, SelfTestSource.Environment,
                fullscreenCycle, play, offscreenProbe, pipeline, shadows, profile, vsync, debugLayer, adapter, windowSize, scatterGrid, propCount,
                loadMapPath, saveMapPath, projectPath, saveProjectPath, bootFromPacks, devContentOverlay,
                exportEntitySchemaPath, exitAfterSave, viewportCompare, pacingProbe, demoCsgAnimation, frameContexts, uncapped, gbufferLayout, pipelineCompare) { Commands = commands };
        }

        return new DemoStartupOptions(
            backend ?? GraphicsBackend.OpenGL, false, SelfTestSource.Default,
            fullscreenCycle, play, offscreenProbe, pipeline, shadows, profile, vsync, debugLayer, adapter, windowSize, scatterGrid, propCount,
                loadMapPath, saveMapPath, projectPath, saveProjectPath, bootFromPacks, devContentOverlay,
                exportEntitySchemaPath, exitAfterSave, viewportCompare, pacingProbe, demoCsgAnimation, frameContexts, uncapped, gbufferLayout, pipelineCompare) { Commands = commands };
    }

    private static string ParseName(string? value, string origin)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{origin}' needs a value, e.g. --pipeline=deferred. {Usage}");

        return value.Trim();
    }

    // "1280x720" or "1280X720".
    private static (int Width, int Height) ParseSize(string? value, string origin)
    {
        string[] parts = (value ?? string.Empty).Split('x', 'X');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out int width) ||
            !int.TryParse(parts[1], out int height) ||
            width <= 0 || height <= 0)
        {
            throw new ArgumentException($"'{origin}' needs a size like --size=1280x720. {Usage}");
        }

        return (width, height);
    }

    private static int ParseCount(string? value, string origin)
    {
        if (!int.TryParse(value, out int count) || count <= 0)
            throw new ArgumentException($"'{origin}' needs a positive count, e.g. --parts=28. {Usage}");

        return count;
    }

    // Same aliases as the ssc CLI.
    private static GraphicsBackend ParseBackend(string value, string token) =>
        value.ToLowerInvariant() switch
        {
            "opengl" or "gl" => GraphicsBackend.OpenGL,
            "d3d11" or "dx11" or "directx11" or "hlsl11" => GraphicsBackend.D3D11,
            "d3d12" or "dx12" or "directx12" or "hlsl12" => GraphicsBackend.D3D12,
            "vulkan" or "vk" => GraphicsBackend.Vulkan,
            _ => throw new ArgumentException($"Unknown backend '{token}'. Try: opengl, d3d11, d3d12."),
        };

    // No value means the harness default.
    private static TimeSpan ParseInterval(string? value, string origin)
    {
        if (value is null)
            return TimeSpan.FromSeconds(FullscreenCycleHarness.DefaultIntervalSeconds);

        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            || double.IsNaN(seconds) || seconds <= 0.0)
        {
            throw new ArgumentException(string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' expects a positive number of seconds, not '{1}'. {2}", origin, value, Usage));
        }

        return TimeSpan.FromSeconds(seconds);
    }

    // A bare switch means on.
    private static bool ParseBoolean(string? value, string origin)
    {
        if (value is null)
            return true;

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new ArgumentException(string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' expects a boolean (true/false, 1/0, yes/no, on/off), not '{1}'. {2}",
                origin, value, Usage)),
        };
    }
}
