using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Packs;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The cooker refuses where the running engine degrades to a default material
/// and a magenta texture.
/// </summary>
// Both behaviours are intended. The engine must keep rendering, the cook must
// stop broken data shipping. Don't make one match the other.
public class CookGateTests
{
    private const string Material = "Materials/wall.spectramat";
    private const string Texture = "Textures/wall_brick.png";

    private const string NamesAMissingTexture =
        "shader = lit\ntexture uDiffuse = Textures/wall_brick.png, linearmipmap, repeat\n";

    [Fact]
    public void The_cooker_refuses_exactly_where_the_running_engine_degrades()
    {
        using var project = new TempProject();
        project.WriteAsset(Material, NamesAMissingTexture);

        CookResult cooked = new CookSession(project.Layout, new CookSettings { UseCache = false }).Run();

        cooked.Succeeded.ShouldBeFalse();
        cooked.OutputPath.ShouldBeNull();

        CookDiagnostic refused = cooked.Diagnostics.Single(d => d.IsError);
        refused.Id.ToString().ShouldBe("SC5001");
        refused.Message.ShouldContain(Texture);

        // The gate makes it fatal, even if a rule reports it as a warning.
        CookGate.Verdict(CookDiagnosticCodes.MaterialTextureMissing).ShouldBe(CookGateVerdict.Fatal);
        CookGate.Apply(
                CookDiagnostic.Warning(CookDiagnosticCodes.MaterialTextureMissing, "as a warning"),
                strict: false)
            .IsError.ShouldBeTrue();

        // Pack built by hand: the cook above refuses to produce one.
        string pack = Path.Combine(project.Root, "hole.spack");
        var writer = new PackWriter();
        writer.Add(Material, PackEntryKind.Material, Encoding.UTF8.GetBytes(NamesAMissingTexture));
        writer.WriteToFile(pack);

        PackVerifyResult verified = PackVerifier.Verify(pack);
        verified.Succeeded.ShouldBeFalse();
        verified.Diagnostics.Single(d => d.IsError).Id.ToString().ShouldBe("SC5001");

        // The runtime's side: the same lookup misses without throwing.
        var mounted = project.Track(new PackSource(NullLogger.Instance, pack));

        var runtime = new ContentSourceStack();
        runtime.Mount(mounted);
        runtime.TryOpen(Texture, out ContentBlob? degraded).ShouldBeFalse();
        degraded.ShouldBeNull();

        var validation = new ContentSourceStack(strict: true);
        validation.Mount(mounted);
        Should.Throw<FileNotFoundException>(() => validation.TryOpen(Texture, out _));
    }

    [Fact]
    public void An_unknown_material_key_warns_by_default_and_fails_under_strict()
    {
        // The parser tolerates unknown keys so material files stay
        // forward-compatible.
        using var project = new TempProject();
        project.WriteAsset(Texture, TempProject.Png(8, 8, seed: 1));
        project.WriteAsset(Material, NamesAMissingTexture + "frobnicate uThing = 3\n");

        CookGate.Verdict(CookDiagnosticCodes.MaterialFileMalformed)
            .ShouldBe(CookGateVerdict.WarningUnlessStrict);

        CookResult lax = Cook(project, strict: false);
        lax.Succeeded.ShouldBeTrue();
        lax.Diagnostics.Single(d => d.Id.ToString() == "SC5002")
            .Severity.ShouldBe(CookDiagnosticSeverity.Warning);

        CookResult strict = Cook(project, strict: true);
        strict.Succeeded.ShouldBeFalse();
        strict.Diagnostics.Single(d => d.Id.ToString() == "SC5002").IsError.ShouldBeTrue();

        // verify honours --strict the same way.
        string pack = lax.OutputPath!;
        PackVerifier.Verify(pack).Succeeded.ShouldBeTrue();
        PackVerifier.Verify(pack, logger: null, targets: null, strict: true).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Strict_never_touches_a_complaint_about_the_run_rather_than_the_data()
    {
        foreach (CookDiagnosticId id in new[]
        {
            CookDiagnosticCodes.OptionNotImplemented,
            CookDiagnosticCodes.CacheNotWritable,
        })
        {
            CookGate.Verdict(id).ShouldBe(CookGateVerdict.Warning);
            CookGate.Apply(CookDiagnostic.Warning(id, "about the run"), strict: true)
                .IsError.ShouldBeFalse($"{id} is about the run, not the data");
        }

        foreach (CookDiagnosticId id in new[]
        {
            CookDiagnosticCodes.ContentNotCooked,
            CookDiagnosticCodes.CacheDiscarded,
        })
        {
            CookGate.Verdict(id).ShouldBe(CookGateVerdict.Note);
            CookGate.Apply(CookDiagnostic.Info(id, "a note"), strict: true)
                .Severity.ShouldBe(CookDiagnosticSeverity.Info);
        }
    }

    [Fact]
    public void A_diagnostic_carrying_another_tools_judgement_keeps_it()
    {
        // SC6001 carries the shader compiler's own severity. Forcing it fatal
        // fails a build over a shader warning; forcing it soft ships a shader
        // that did not compile.
        CookGate.Verdict(CookDiagnosticCodes.ShaderCompileFailed).ShouldBe(CookGateVerdict.AsReported);

        CookGate.Apply(CookDiagnostic.Error(CookDiagnosticCodes.ShaderCompileFailed, "syntax"), strict: false)
            .IsError.ShouldBeTrue();

        CookGate.Apply(CookDiagnostic.Warning(CookDiagnosticCodes.ShaderCompileFailed, "unused"), strict: false)
            .Severity.ShouldBe(CookDiagnosticSeverity.Warning);

        CookGate.Apply(CookDiagnostic.Warning(CookDiagnosticCodes.ShaderCompileFailed, "unused"), strict: true)
            .IsError.ShouldBeTrue();

        CookGate.Verdict(CookDiagnosticId.Wrap("SS", 42)).ShouldBe(CookGateVerdict.AsReported);
    }

    [Fact]
    public void Every_code_the_cooker_owns_has_a_verdict()
    {
        // An unclassified code defaults to Fatal, so it would go unnoticed
        // without this.
        List<CookDiagnosticId> codes = DeclaredCodes();
        codes.Count.ShouldBeGreaterThan(20, "the reflection should find the declared code table");

        var unclassified = codes.Where(id => !CookGate.IsClassified(id)).ToList();

        unclassified.ShouldBeEmpty(
            "every SC#### the cooker owns needs one line in CookGate's table: " +
            string.Join(", ", unclassified));
    }

    [Fact]
    public void The_gate_does_not_reclassify_a_diagnostic_it_already_agrees_with()
    {
        CookDiagnostic already = CookDiagnostic.Error(CookDiagnosticCodes.PackNotMountable, "refused");
        CookGate.Apply(already, strict: false).ShouldBeSameAs(already);
    }

    private static CookResult Cook(TempProject project, bool strict) =>
        new CookSession(
                project.Layout,
                new CookSettings
                {
                    UseCache = false,
                    Strict = strict,
                    OutputPath = Path.Combine(project.Root, strict ? "strict" : "lax"),
                })
            .Run();

    // Reflection is fine in a test assembly: it is never trimmed.
    private static List<CookDiagnosticId> DeclaredCodes()
    {
        var codes = new List<CookDiagnosticId>();
        foreach (FieldInfo field in typeof(CookDiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(CookDiagnosticId)) continue;

            codes.Add((CookDiagnosticId)field.GetValue(null)!);
        }

        return codes;
    }
}
