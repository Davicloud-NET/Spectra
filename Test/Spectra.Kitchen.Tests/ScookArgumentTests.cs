using Spectra.Kitchen.CLI;
using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Graphics;
using System.IO;

namespace Spectra.Kitchen.Tests;

// How scook reads its arguments: what each switch sets, and the words a
// usage error is refused with.
public class ScookArgumentTests
{
    [Theory]
    [InlineData("-o", "'-o' requires a path")]
    [InlineData("--output", "'--output' requires a path")]
    [InlineData("--profile", "'--profile' requires a profile")]
    [InlineData("--profile shipp", "unknown profile 'shipp'. Valid: ship, fast, preview")]
    [InlineData("-t", "'-t' requires a backend")]
    [InlineData("--target metal", "unknown target backend 'metal'. Valid: opengl, vulkan, d3d11, d3d12, all")]
    [InlineData("-j", "'-j' requires a worker count")]
    [InlineData("--jobs 0", "'--jobs' requires a worker count of 1 or more, not '0'")]
    [InlineData("-j many", "'-j' requires a worker count of 1 or more, not 'many'")]
    [InlineData("--script-source", "'--script-source' requires embed or strip")]
    [InlineData("--script-source keep", "unknown script source mode 'keep'. Valid: embed, strip")]
    [InlineData("--encoder", "'--encoder' requires managed or native")]
    [InlineData("--encoder gpu", "unknown encoder 'gpu'. Valid: managed, native")]
    [InlineData("--manifest", "'--manifest' requires a path")]
    [InlineData("--", "'--' must be followed by a path")]
    [InlineData("here -- there", "more than one path specified")]
    [InlineData("here there", "more than one path specified")]
    [InlineData("here clean", "more than one path specified")]
    [InlineData("--frobnicate", "unknown option: --frobnicate")]
    [InlineData("--frobnicate --help", "unknown option: --frobnicate")]
    [InlineData("cook here --json", "'--json' is only meaningful for 'inspect', not 'cook'")]
    [InlineData("sounds here --json -o out", "'--json' is only meaningful for 'inspect', not 'sounds'")]
    [InlineData("verify", "'verify' requires a path to a pack")]
    [InlineData("inspect --json", "'inspect' requires a path to a pack")]
    [InlineData("sounds here", "'sounds' requires -o, the folder the cooked sounds go to")]
    public void A_usage_error_says_what_is_wrong(string line, string error)
    {
        ParseResult parsed = Parse(line);

        parsed.Mode.ShouldBe(CliMode.UsageError);
        parsed.Error.ShouldBe(error);
        parsed.Options.ShouldBeNull();
    }

    [Theory]
    [InlineData("-h", true)]
    [InlineData("--help", true)]
    [InlineData("/?", true)]
    [InlineData("cook here --help --frobnicate", true)]
    [InlineData("--version", false)]
    [InlineData("--version --help", false)]
    public void Help_and_version_win_over_whatever_follows_them(string line, bool isHelp)
    {
        Parse(line).Mode.ShouldBe(isHelp ? CliMode.Help : CliMode.Version);
    }

    [Fact]
    public void No_arguments_cook_the_current_folder_with_the_defaults()
    {
        CliOptions options = Options("--no-color");

        options.Verb.ShouldBe(CliVerb.Cook);
        options.Target.ShouldBe(Directory.GetCurrentDirectory());
        options.Output.ShouldBeNull();
        options.Profile.ShouldBe(CookProfile.Ship);
        options.Targets.ShouldBeEmpty();
        options.Jobs.ShouldBe(1);
        options.UseCache.ShouldBeTrue();
        options.ScriptSource.ShouldBe(ScriptSourceMode.Embed);
        options.Encoder.ShouldBe(CookEncoder.Managed);
        options.ManifestPath.ShouldBeNull();
        options.UseColor.ShouldBeFalse();

        new[]
        {
            options.Loose, options.Watch, options.KeepBrushSource, options.Strict, options.Quiet, options.Json,
            options.ProfileGiven, options.TargetsGiven, options.JobsGiven, options.CacheGiven,
        }.ShouldAllBe(set => !set);
    }

    [Fact]
    public void Every_switch_of_a_cook_lands_in_its_option()
    {
        CliOptions options = Options(
            "cook here -o out --profile fast -t gl,dx12 -t gl -j 4 --no-cache --loose --watch " +
            "--keep-brush-source --script-source strip --encoder native --strict --manifest m.json -q --no-color");

        options.Verb.ShouldBe(CliVerb.Cook);
        options.Target.ShouldBe("here");
        options.Output.ShouldBe("out");
        options.Profile.ShouldBe(CookProfile.Fast);
        options.Targets.ShouldBe([GraphicsBackend.OpenGL, GraphicsBackend.D3D12]);
        options.Jobs.ShouldBe(4);
        options.UseCache.ShouldBeFalse();
        options.ScriptSource.ShouldBe(ScriptSourceMode.Strip);
        options.Encoder.ShouldBe(CookEncoder.Native);
        options.ManifestPath.ShouldBe("m.json");
        options.UseColor.ShouldBeFalse();

        new[]
        {
            options.Loose, options.Watch, options.KeepBrushSource, options.Strict, options.Quiet,
            options.ProfileGiven, options.TargetsGiven, options.JobsGiven, options.CacheGiven,
        }.ShouldAllBe(set => set);

        options.Json.ShouldBeFalse();
    }

    [Fact]
    public void The_long_spellings_and_the_later_switch_are_taken()
    {
        CliOptions options = Options(
            "--output out --target all --jobs 2 --no-cache --cache --quiet --profile preview here");

        options.Output.ShouldBe("out");
        options.Targets.ShouldBe(
            [GraphicsBackend.OpenGL, GraphicsBackend.Vulkan, GraphicsBackend.D3D11, GraphicsBackend.D3D12]);
        options.Jobs.ShouldBe(2);
        options.UseCache.ShouldBeTrue();
        options.CacheGiven.ShouldBeTrue();
        options.Quiet.ShouldBeTrue();
        options.Profile.ShouldBe(CookProfile.Preview);
        options.Target.ShouldBe("here");
    }

    [Theory]
    [InlineData("verify some.spack", "verify", "some.spack")]
    [InlineData("inspect some.spack --json", "inspect", "some.spack")]
    [InlineData("clean here", "clean", "here")]
    [InlineData("sounds here -o out", "sounds", "here")]
    [InlineData("-o out sounds here", "sounds", "here")]
    // Only the first bare word can be the verb.
    [InlineData("cook verify", "cook", "verify")]
    // A folder named like a verb, after the two dashes.
    [InlineData("-- cook", "cook", "cook")]
    [InlineData("verify -- sounds", "verify", "sounds")]
    public void The_first_bare_word_is_the_verb_when_it_names_one(string line, string verb, string target)
    {
        CliOptions options = Options(line);

        CliParser.ToWire(options.Verb).ShouldBe(verb);
        options.Target.ShouldBe(target);
        options.Json.ShouldBe(line.Contains("--json"));
    }

    private static ParseResult Parse(string line) =>
        CliParser.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static CliOptions Options(string line)
    {
        ParseResult parsed = Parse(line);
        parsed.Mode.ShouldBe(CliMode.Run, parsed.Error);

        return parsed.Options.ShouldNotBeNull();
    }
}
