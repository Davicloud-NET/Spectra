using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Windowing;
using System;

namespace SpectraEngine.Core.Projects;

/// <summary>
/// The project file's constants: its name, its member names, and the closed
/// vocabularies it shares with the command line.
/// </summary>
public static class ProjectFormat
{
    /// <summary>Extension of the project manifest.</summary>
    public const string Extension = ".spectraproj";

    /// <summary>Suffix of per-user project state. Gitignored.</summary>
    public const string UserStateSuffix = ".user";

    /// <summary>The content root.</summary>
    public const string AssetsFolder = "Assets";

    /// <summary>Where map bundles live.</summary>
    public const string MapsFolder = "Maps";

    /// <summary>Project-level shared script modules.</summary>
    public const string ScriptsFolder = "Scripts";

    /// <summary>Cook output. Derived and gitignored.</summary>
    public const string CookedFolder = "cooked";

    public const string FormatVersionMember = "spectraproject";
    public const string MinimumReadableMember = "minimumReadableVersion";
    public const string EngineMember = "engine";
    public const string NameMember = "name";
    public const string IdMember = "id";
    public const string LanguageMember = "language";
    public const string StartupMapMember = "startupMap";
    public const string MapsMember = "maps";
    public const string PacksMember = "packs";
    public const string DisplayMember = "display";
    public const string DefaultBackendMember = "defaultBackend";
    public const string AllowedBackendsMember = "allowedBackends";

    public const string WidthMember = "width";
    public const string HeightMember = "height";
    public const string VsyncMember = "vsync";
    public const string ModeMember = "mode";

    // Same spellings as the command line.
    internal static string ToWire(GraphicsBackend backend) => backend switch
    {
        GraphicsBackend.OpenGL => "opengl",
        GraphicsBackend.Vulkan => "vulkan",
        GraphicsBackend.D3D11 => "d3d11",
        GraphicsBackend.D3D12 => "d3d12",
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown backend."),
    };

    internal static bool TryParseBackend(string value, out GraphicsBackend backend)
    {
        switch (value)
        {
            case "opengl": backend = GraphicsBackend.OpenGL; return true;
            case "vulkan": backend = GraphicsBackend.Vulkan; return true;
            case "d3d11": backend = GraphicsBackend.D3D11; return true;
            case "d3d12": backend = GraphicsBackend.D3D12; return true;
            default: backend = default; return false;
        }
    }

    internal static string ToWire(WindowMode mode) =>
        mode == WindowMode.BorderlessFullscreen ? "fullscreen" : "windowed";

    internal static bool TryParseWindowMode(string value, out WindowMode mode)
    {
        switch (value)
        {
            case "windowed": mode = WindowMode.Windowed; return true;
            case "fullscreen": mode = WindowMode.BorderlessFullscreen; return true;
            default: mode = default; return false;
        }
    }
}
