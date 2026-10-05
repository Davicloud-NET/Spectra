using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Graphics;
using System.Globalization;

namespace Spectra.Kitchen.CLI;

// Reads scook's arguments into CliOptions. One instance reads one command
// line: its fields are what has been read so far.
internal sealed class CliParser
{
    private readonly List<GraphicsBackend> _targets = [];

    private CliVerb? _verb;
    private string? _target;
    private string? _output;
    private string? _manifest;
    private CookProfile _profile = CookProfile.Ship;
    private ScriptSourceMode _scriptSource = ScriptSourceMode.Embed;
    private CookEncoder _encoder = CookEncoder.Managed;
    private int _jobs = 1;
    private bool _useCache = true;
    private bool _loose, _watch, _keepBrush, _strict, _quiet, _noColor, _json;
    private bool _profileGiven, _jobsGiven, _cacheGiven;

    private CliParser()
    {
    }

    public static ParseResult Parse(string[] args)
    {
        var parser = new CliParser();

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a is "-h" or "--help" or "/?") return ParseResult.ForMode(CliMode.Help);
            if (a == "--version") return ParseResult.ForMode(CliMode.Version);

            if (parser.Take(args, ref i) is { } error) return ParseResult.Usage(error);
        }

        return parser.Finish();
    }

    public static string ToWire(CliVerb verb) => verb switch
    {
        CliVerb.Cook => "cook",
        CliVerb.Verify => "verify",
        CliVerb.Inspect => "inspect",
        CliVerb.Clean => "clean",
        CliVerb.Sounds => "sounds",
        _ => "cook",
    };

    // Reads the argument at i, and the one after it when it is that
    // argument's value. Null when it was read, otherwise what is wrong.
    private string? Take(string[] args, ref int i)
    {
        string a = args[i];
        if (TakeSwitch(a)) return null;

        if (ValueNeeded(a) is { } needed)
            return i + 1 < args.Length ? TakeValue(a, args[++i]) : $"'{a}' requires {needed}";

        if (a == "--")
            return i + 1 < args.Length ? TakeTarget(args[++i]) : "'--' must be followed by a path";

        if (a.StartsWith('-')) return $"unknown option: {a}";

        // First bare word is the verb if it names one. A folder called "cook"
        // is reachable as './cook' or after '--'.
        if (_verb is null && _target is null && TryParseVerb(a, out CliVerb verb))
        {
            _verb = verb;
            return null;
        }

        return TakeTarget(a);
    }

    private bool TakeSwitch(string a)
    {
        switch (a)
        {
            case "--cache":
                _useCache = true;
                _cacheGiven = true;
                return true;
            case "--no-cache":
                _useCache = false;
                _cacheGiven = true;
                return true;
            case "--loose":
                _loose = true;
                return true;
            case "--watch":
                _watch = true;
                return true;
            case "--keep-brush-source":
                _keepBrush = true;
                return true;
            case "--strict":
                _strict = true;
                return true;
            case "--json":
                _json = true;
                return true;
            case "-q":
            case "--quiet":
                _quiet = true;
                return true;
            case "--no-color":
                _noColor = true;
                return true;
            default:
                return false;
        }
    }

    // What a switch that takes a value needs after it. Null for any other argument.
    private static string? ValueNeeded(string a) => a switch
    {
        "-o" or "--output" or "--manifest" => "a path",
        "--profile" => "a profile",
        "-t" or "--target" => "a backend",
        "-j" or "--jobs" => "a worker count",
        "--script-source" => "embed or strip",
        "--encoder" => "managed or native",
        _ => null,
    };

    // For a switch ValueNeeded knows. Null when the value was read.
    private string? TakeValue(string option, string value)
    {
        switch (option)
        {
            case "-o":
            case "--output":
                _output = value;
                return null;
            case "--manifest":
                _manifest = value;
                return null;
            case "--profile":
                if (!TryParseProfile(value, out _profile))
                    return $"unknown profile '{value}'. Valid: ship, fast, preview";
                _profileGiven = true;
                return null;
            case "-t":
            case "--target":
                return TryParseTargets(value, _targets, out string targetError) ? null : targetError;
            case "-j":
            case "--jobs":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _jobs) || _jobs < 1)
                    return $"'{option}' requires a worker count of 1 or more, not '{value}'";
                _jobsGiven = true;
                return null;
            case "--script-source":
                return TryParseScriptSource(value, out _scriptSource)
                    ? null
                    : $"unknown script source mode '{value}'. Valid: embed, strip";
            default:
                return TryParseEncoder(value, out _encoder)
                    ? null
                    : $"unknown encoder '{value}'. Valid: managed, native";
        }
    }

    private string? TakeTarget(string path)
    {
        if (_target is not null) return "more than one path specified";

        _target = path;
        return null;
    }

    private ParseResult Finish()
    {
        CliVerb verb = _verb ?? CliVerb.Cook;

        // Refuse rather than ignore a switch on the wrong verb.
        if (_json && verb != CliVerb.Inspect)
            return ParseResult.Usage($"'--json' is only meaningful for 'inspect', not '{ToWire(verb)}'");

        if (_target is null && verb is CliVerb.Verify or CliVerb.Inspect)
            return ParseResult.Usage($"'{ToWire(verb)}' requires a path to a pack");

        // No default: beside the sources, a later project cook would find
        // every sound twice.
        if (verb == CliVerb.Sounds && _output is null)
            return ParseResult.Usage("'sounds' requires -o, the folder the cooked sounds go to");

        return ParseResult.ForOptions(new CliOptions
        {
            Verb = verb,
            Target = _target ?? Directory.GetCurrentDirectory(),
            Output = _output,
            Profile = _profile,
            Targets = _targets,
            Jobs = _jobs,
            UseCache = _useCache,
            Loose = _loose,
            Watch = _watch,
            KeepBrushSource = _keepBrush,
            ScriptSource = _scriptSource,
            Encoder = _encoder,
            Strict = _strict,
            ManifestPath = _manifest,
            Quiet = _quiet,
            UseColor = !_noColor && ConsoleColor.ShouldUseForStderr(),
            Json = _json,
            ProfileGiven = _profileGiven,
            TargetsGiven = _targets.Count > 0,
            JobsGiven = _jobsGiven,
            CacheGiven = _cacheGiven,
        });
    }

    // Not Enum.Parse: enum names do not survive trimming.
    private static bool TryParseVerb(string value, out CliVerb verb)
    {
        switch (value)
        {
            case "cook": verb = CliVerb.Cook; return true;
            case "verify": verb = CliVerb.Verify; return true;
            case "inspect": verb = CliVerb.Inspect; return true;
            case "clean": verb = CliVerb.Clean; return true;
            case "sounds": verb = CliVerb.Sounds; return true;
            default: verb = CliVerb.Cook; return false;
        }
    }

    private static bool TryParseProfile(string value, out CookProfile profile)
    {
        switch (value)
        {
            case "ship": profile = CookProfile.Ship; return true;
            case "fast": profile = CookProfile.Fast; return true;
            case "preview": profile = CookProfile.Preview; return true;
            default: profile = CookProfile.Ship; return false;
        }
    }

    private static bool TryParseScriptSource(string value, out ScriptSourceMode mode)
    {
        switch (value)
        {
            case "embed": mode = ScriptSourceMode.Embed; return true;
            case "strip": mode = ScriptSourceMode.Strip; return true;
            default: mode = ScriptSourceMode.Embed; return false;
        }
    }

    private static bool TryParseEncoder(string value, out CookEncoder encoder)
    {
        switch (value)
        {
            case "managed": encoder = CookEncoder.Managed; return true;
            case "native": encoder = CookEncoder.Native; return true;
            default: encoder = CookEncoder.Managed; return false;
        }
    }

    // Same grammar and aliases as ssc.
    private static bool TryParseTargets(string value, List<GraphicsBackend> into, out string error)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var raw in parts)
        {
            var token = raw.ToLowerInvariant();
            if (token == "all")
            {
                AddUnique(into, GraphicsBackend.OpenGL);
                AddUnique(into, GraphicsBackend.Vulkan);
                AddUnique(into, GraphicsBackend.D3D11);
                AddUnique(into, GraphicsBackend.D3D12);
                continue;
            }

            var backend = token switch
            {
                "opengl" or "gl" or "glsl" => (GraphicsBackend?)GraphicsBackend.OpenGL,
                "vulkan" or "vk" or "spirv" => GraphicsBackend.Vulkan,
                "d3d11" or "dx11" or "hlsl11" => GraphicsBackend.D3D11,
                "d3d12" or "dx12" or "hlsl12" => GraphicsBackend.D3D12,
                _ => null,
            };
            if (backend is null)
            {
                error = $"unknown target backend '{raw}'. Valid: opengl, vulkan, d3d11, d3d12, all";
                return false;
            }
            AddUnique(into, backend.Value);
        }
        error = string.Empty;
        return true;
    }

    private static void AddUnique(List<GraphicsBackend> list, GraphicsBackend item)
    {
        if (!list.Contains(item))
            list.Add(item);
    }
}
