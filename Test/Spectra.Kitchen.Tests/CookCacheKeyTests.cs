using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Rules;
using System;
using System.Buffers.Binary;
using System.Text;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// What goes into a cook cache key, field by field. Layout is checked on the
/// canonical stream, since a moved hash does not say which field moved it.
/// </summary>
public class CookCacheKeyTests
{
    private static readonly RuleDependency[] OneRead =
    [
        new("Textures/a.png", RuleDependencyKind.Read, (UInt128)0x1234),
    ];

    [Fact]
    public void The_stream_opens_with_the_tag_and_the_three_versions_it_is_specified_to_carry()
    {
        byte[] stream = Stream(RuleKind.RawCopy, ruleVersion: 7, CookSettingKeys.None, new CookSettings());

        Encoding.UTF8.GetString(stream, 0, 6).ShouldBe("SCOOK\0");
        BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(6)).ShouldBe(CookCacheKey.CookerVersion);
        BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(10)).ShouldBe((uint)RuleKind.RawCopy);
        BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(14)).ShouldBe(7u);
    }

    [Fact]
    public void The_rule_kind_and_the_rule_version_are_both_in_the_key()
    {
        UInt128 baseline = Key(RuleKind.RawCopy, 1, CookSettingKeys.None, new CookSettings());

        Key(RuleKind.RawCopy, 2, CookSettingKeys.None, new CookSettings()).ShouldNotBe(baseline);
        Key(RuleKind.Image, 1, CookSettingKeys.None, new CookSettings()).ShouldNotBe(baseline);
    }

    [Fact]
    public void An_inputs_contents_are_in_the_key_and_so_is_the_order_it_was_read_in()
    {
        RuleDependency a = new("Textures/a.png", RuleDependencyKind.Read, (UInt128)1);
        RuleDependency b = new("Textures/b.png", RuleDependencyKind.Read, (UInt128)2);

        UInt128 baseline = Key([a, b]);

        RuleDependency changed = a with { ContentHash = (UInt128)99 };
        Key([changed, b]).ShouldNotBe(baseline);

        // Not sorted: a sort would hide a rule whose read order varies by schedule.
        Key([b, a]).ShouldNotBe(baseline);
    }

    [Fact]
    public void Adding_a_file_a_rule_probed_and_missed_gives_that_rule_a_different_key()
    {
        RuleDependency source = new("Materials/wall.spectramat", RuleDependencyKind.Read, (UInt128)7);
        const string Texture = "Textures/wall_brick.png";

        UInt128 missed = Key([source, new(Texture, RuleDependencyKind.ProbeMissing, UInt128.Zero)]);
        UInt128 found = Key([source, new(Texture, RuleDependencyKind.ProbeFound, UInt128.Zero)]);

        found.ShouldNotBe(missed);
    }

    [Fact]
    public void A_setting_moves_the_key_only_for_a_rule_that_declared_it()
    {
        var ship = new CookSettings { Profile = CookProfile.Ship };
        var fast = new CookSettings { Profile = CookProfile.Fast };

        UInt128 readsProfileUnderShip = Key(RuleKind.Map, 1, CookSettingKeys.Profile, ship);
        UInt128 readsProfileUnderFast = Key(RuleKind.Map, 1, CookSettingKeys.Profile, fast);
        readsProfileUnderFast.ShouldNotBe(readsProfileUnderShip);

        UInt128 ignoresProfileUnderShip = Key(RuleKind.RawCopy, 1, CookSettingKeys.None, ship);
        UInt128 ignoresProfileUnderFast = Key(RuleKind.RawCopy, 1, CookSettingKeys.None, fast);
        ignoresProfileUnderFast.ShouldBe(ignoresProfileUnderShip);

        // A rule that declares only ScriptSource ignores a profile switch too.
        var strip = new CookSettings { Profile = CookProfile.Fast, ScriptSource = ScriptSourceMode.Strip };
        Key(RuleKind.Script, 1, CookSettingKeys.ScriptSource, fast)
            .ShouldBe(Key(RuleKind.Script, 1, CookSettingKeys.ScriptSource, ship));
        Key(RuleKind.Script, 1, CookSettingKeys.ScriptSource, strip)
            .ShouldNotBe(Key(RuleKind.Script, 1, CookSettingKeys.ScriptSource, ship));
    }

    [Fact]
    public void Settings_that_cannot_change_a_cooked_payload_are_not_in_any_key()
    {
        var plain = new CookSettings();
        var everythingElse = new CookSettings
        {
            Jobs = 8,
            Loose = true,
            Strict = true,
            UseCache = false,
            OutputPath = "somewhere/else",
            ManifestPath = "cook.json",
        };

        // Scheduling and output settings. No rule can declare these.
        Key(RuleKind.Map, 1, AllSettingKeys, everythingElse)
            .ShouldBe(Key(RuleKind.Map, 1, AllSettingKeys, plain));
    }

    [Fact]
    public void The_instruction_set_baseline_rides_in_every_key()
    {
        string token = InstructionSetBaseline.Token;

        token.ShouldNotBeNullOrWhiteSpace();
        token.ShouldContain("avx2=");
        (token.StartsWith("jit;", StringComparison.Ordinal) ||
         token.StartsWith("aot;", StringComparison.Ordinal)).ShouldBeTrue(token);

        // BC7 output differs byte for byte with and without AVX2.
        byte[] stream = Stream(RuleKind.Image, 1, CookSettingKeys.None, new CookSettings());
        IndexOf(stream, Encoding.UTF8.GetBytes(token)).ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void The_format_versions_of_every_artifact_a_rule_could_emit_are_in_the_key()
    {
        byte[] stream = Stream(RuleKind.Map, 1, CookSettingKeys.None, new CookSettings());

        foreach (string tool in new[] { "encoder", "shaderFormat", "mapFormat", "geometryFormat", "packFormat", "isa" })
            IndexOf(stream, Encoding.UTF8.GetBytes(tool)).ShouldBeGreaterThanOrEqualTo(0, tool);
    }

    private const CookSettingKeys AllSettingKeys =
        CookSettingKeys.Profile | CookSettingKeys.Targets | CookSettingKeys.ScriptSource |
        CookSettingKeys.Encoder | CookSettingKeys.KeepBrushSource;

    private static byte[] Stream(
        RuleKind kind, int ruleVersion, CookSettingKeys declared, CookSettings settings) =>
        CookCacheKey.BuildCanonicalStream(kind, ruleVersion, declared, settings, OneRead);

    private static UInt128 Key(
        RuleKind kind, int ruleVersion, CookSettingKeys declared, CookSettings settings) =>
        CookCacheKey.Compute(kind, ruleVersion, declared, settings, OneRead);

    private static UInt128 Key(RuleDependency[] dependencies) =>
        CookCacheKey.Compute(RuleKind.RawCopy, 1, CookSettingKeys.None, new CookSettings(), dependencies);

    private static int IndexOf(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle);
}
