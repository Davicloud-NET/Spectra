using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Graphics;

namespace Spectra.Kitchen.CLI;

internal sealed class CliOptions
{
    public required CliVerb Verb { get; init; }

    // Project folder or manifest for cook and clean; the pack for verify and
    // inspect; the content folder for sounds.
    public required string Target { get; init; }

    public string? Output { get; init; }
    public CookProfile Profile { get; init; } = CookProfile.Ship;
    public IReadOnlyList<GraphicsBackend> Targets { get; init; } = [];
    public int Jobs { get; init; } = 1;
    public bool UseCache { get; init; } = true;
    public bool Loose { get; init; }
    public bool Watch { get; init; }
    public bool KeepBrushSource { get; init; }
    public ScriptSourceMode ScriptSource { get; init; } = ScriptSourceMode.Embed;
    public CookEncoder Encoder { get; init; } = CookEncoder.Managed;
    public bool Strict { get; init; }
    public string? ManifestPath { get; init; }
    public bool Quiet { get; init; }
    public bool UseColor { get; init; }

    public bool Json { get; init; }

    // Whether the switch was typed, as opposed to left at its default.
    public bool ProfileGiven { get; init; }
    public bool TargetsGiven { get; init; }
    public bool JobsGiven { get; init; }
    public bool CacheGiven { get; init; }

    public CookSettings ToCookSettings() => new()
    {
        OutputPath = Output,
        Profile = Profile,
        Targets = Targets.Count > 0 ? Targets : DefaultTargets(),
        Jobs = Jobs,
        UseCache = UseCache,
        // --watch implies --loose
        Loose = Loose || Watch,
        KeepBrushSource = KeepBrushSource,
        ScriptSource = ScriptSource,
        Encoder = Encoder,
        Strict = Strict,
        ManifestPath = ManifestPath,
    };

    // Same three as ssc. No Vulkan until SPIR-V emission exists.
    public static IReadOnlyList<GraphicsBackend> DefaultTargets() =>
        [GraphicsBackend.OpenGL, GraphicsBackend.D3D11, GraphicsBackend.D3D12];
}
