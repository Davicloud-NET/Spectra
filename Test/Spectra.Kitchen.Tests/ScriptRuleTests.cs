using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Packs;
using Spectra.Kitchen.Rules;
using Spectra.Kitchen.Scripts;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scripting;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The script cook: a script that compiles is packed as its text behind a
/// header, and one that does not, or that nothing can check, fails the cook.
/// </summary>
// The check is a fake here. The real one is tested where Luau is, and
// ScookCliTests cooks through it.
public class ScriptRuleTests
{
    private const string Door = "Scripts/door.luau";
    private const string Lamp = "Scripts/Lib/lamp.luau";

    private const string HowToBuild =
        "The Luau library was not found. Build it with native/build-luau.ps1 on Windows or " +
        "native/build-luau.sh on Linux.";

    [Fact]
    public void A_script_that_compiles_is_packed_at_its_own_path_as_a_script()
    {
        using var project = new TempProject();
        byte[] text = project.WriteAsset(Door, "print(\"open\")\n");

        RuleContext context = Run(project, Door, new FakeScriptCheck());

        context.Diagnostics.ShouldBeEmpty();

        RuleEmission packed = context.Emissions.ShouldHaveSingleItem();
        packed.Path.ShouldBe(Door);
        packed.Kind.ShouldBe(PackEntryKind.Script);
        PackedScript.SourceOf(packed.Payload, Door).ToArray().ShouldBe(text);
    }

    [Fact]
    public void A_packed_script_is_a_header_of_sixteen_bytes_and_then_the_text()
    {
        byte[] entry = PackedScriptWriter.WriteSource("x=1"u8);

        entry.ShouldBe(
        [
            0x1B, (byte)'S', (byte)'L', (byte)'U',
            1, 0,                   // version
            1,                      // the payload is text
            0,
            3, 0, 0, 0,             // three bytes of it
            0, 0, 0, 0,
            (byte)'x', (byte)'=', (byte)'1',
        ]);

        PackedScript.HeaderSize.ShouldBe(16);
        PackedScript.IsPacked(entry).ShouldBeTrue();
    }

    [Fact]
    public void The_text_is_packed_and_checked_as_the_file_holds_it()
    {
        // As a Windows editor saves it: a byte order mark and CRLF.
        byte[] saved = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("local a = 1\r\nprint(a)\r\n")];

        using var project = new TempProject();
        project.WriteAsset(Door, saved);
        var check = new FakeScriptCheck();

        RuleContext context = Run(project, Door, check);

        PackedScript.SourceOf(context.Emissions[0].Payload, Door).ToArray().ShouldBe(saved);
        check.Asked.ShouldHaveSingleItem().ShouldBe(saved);
    }

    [Fact]
    public void A_script_with_a_mistake_fails_the_cook_at_its_file_line_and_column()
    {
        using var project = new TempProject();
        project.WriteAsset(Door, "local a = 1\nlocal b = 2\n  oops\n");

        CookResult cooked = Cook(project, new FakeScriptCheck());

        cooked.Succeeded.ShouldBeFalse();
        cooked.OutputPath.ShouldBeNull();

        CookDiagnostic mistake = cooked.Diagnostics.Single(d => d.IsError);
        mistake.Id.ToString().ShouldBe("SC8001");
        mistake.File.ShouldBe(Door);
        mistake.Line.ShouldBe(3);
        mistake.Column.ShouldBe(3);
        mistake.ToBuildLine("scook").ShouldBe("Scripts/door.luau(3,3): error SC8001: 'oops' is no statement");

        CookGate.Verdict(CookDiagnosticCodes.ScriptSyntaxError).ShouldBe(CookGateVerdict.Fatal);
    }

    [Fact]
    public void A_compiler_that_names_no_column_points_at_the_start_of_the_line()
    {
        using var project = new TempProject();
        project.WriteAsset(Door, "local a = 1\n  oops\n");

        RuleContext context = Run(project, Door, new FakeScriptCheck { NamesColumns = false });

        CookDiagnostic mistake = context.Diagnostics.ShouldHaveSingleItem();
        mistake.Line.ShouldBe(2);
        mistake.Column.ShouldBe(0);
        mistake.ToBuildLine("scook").ShouldStartWith("Scripts/door.luau(2,1): error SC8001:");
        context.Emissions.ShouldBeEmpty();
    }

    [Fact]
    public void One_script_with_a_mistake_stops_the_pack_and_the_others_are_still_checked()
    {
        using var project = new TempProject();
        project.WriteAsset(Door, "oops\n");
        project.WriteAsset(Lamp, "print(1)\n");
        var check = new FakeScriptCheck();

        CookResult cooked = Cook(project, check);

        cooked.Succeeded.ShouldBeFalse();
        cooked.Diagnostics.Count(d => d.IsError).ShouldBe(1);
        check.Asked.Length.ShouldBe(2);
    }

    [Fact]
    public void A_cook_that_cannot_check_a_script_fails_and_says_how_to_build_the_compiler()
    {
        using var project = new TempProject();
        project.WriteAsset(Door, "print(1)\n");
        var check = new FakeScriptCheck { Problem = HowToBuild };

        CookResult cooked = Cook(project, check);

        cooked.Succeeded.ShouldBeFalse();
        cooked.OutputPath.ShouldBeNull();

        CookDiagnostic refused = cooked.Diagnostics.Single(d => d.IsError);
        refused.Id.ToString().ShouldBe("SC8002");
        refused.File.ShouldBe(Door);
        refused.Message.ShouldContain(Door);
        refused.Message.ShouldContain("native/build-luau.ps1");

        check.Asked.ShouldBeEmpty();
        CookGate.Verdict(CookDiagnosticCodes.ScriptNotChecked).ShouldBe(CookGateVerdict.Fatal);
    }

    [Fact]
    public void A_cook_whose_host_installed_no_check_refuses_a_project_with_a_script()
    {
        using var project = new TempProject();
        project.WriteAsset(Door, "print(1)\n");

        CookResult cooked = WithNoCheck(project);

        cooked.Succeeded.ShouldBeFalse();
        cooked.Diagnostics.Single(d => d.IsError).Id.ToString().ShouldBe("SC8002");
    }

    [Fact]
    public void A_project_with_no_script_cooks_with_no_check_installed()
    {
        using var project = new TempProject();
        project.WriteAsset("Data/notes.txt", "hello\n");

        WithNoCheck(project).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Scripts/door.luau", true)]
    [InlineData("Scripts/DOOR.LUAU", true)]
    [InlineData("door.luau", true)]
    [InlineData("Scripts/door.lua", false)]
    [InlineData("Scripts/door.luau.txt", false)]
    [InlineData("Scripts/luau", false)]
    public void A_file_is_a_script_by_its_extension(string path, bool isScript)
    {
        ScriptRule.Handles(path).ShouldBe(isScript);

        IRule rule = new CookRuleSet("en", new FakeScriptCheck()).Resolve(path);
        (rule is ScriptRule).ShouldBe(isScript);
    }

    [Fact]
    public void A_checked_script_comes_from_the_cache_and_is_not_asked_about_again()
    {
        using var project = new TempProject();
        project.WriteAsset(Door, "print(1)\n");
        var check = new FakeScriptCheck();

        CookResult first = Cook(project, check, cache: true, label: "first");
        CookResult second = Cook(project, check, cache: true, label: "second");

        second.CacheHits.ShouldBe(1);
        check.Asked.Length.ShouldBe(1);
        File.ReadAllBytes(second.OutputPath.ShouldNotBeNull())
            .ShouldBe(File.ReadAllBytes(first.OutputPath.ShouldNotBeNull()));
    }

    [Fact]
    public void A_script_that_failed_is_asked_about_again_on_the_next_cook()
    {
        using var project = new TempProject();
        project.WriteAsset(Door, "oops\n");
        var check = new FakeScriptCheck();

        Cook(project, check, cache: true).Succeeded.ShouldBeFalse();
        Cook(project, check, cache: true).Succeeded.ShouldBeFalse();

        check.Asked.Length.ShouldBe(2);
    }

    [Fact]
    public void Another_release_of_the_compiler_checks_a_cached_script_again()
    {
        using var project = new TempProject();
        project.WriteAsset(Door, "print(1)\n");
        project.WriteAsset("Data/notes.txt", "hello\n");

        Cook(project, new FakeScriptCheck { CompilerVersion = "fake 1" }, cache: true).CacheMisses.ShouldBe(2);

        var newer = new FakeScriptCheck { CompilerVersion = "fake 2" };
        CookResult recooked = Cook(project, newer, cache: true);

        // The release is a tool version, so it is in the key of the copy too.
        recooked.CacheHits.ShouldBe(0);
        recooked.CacheMisses.ShouldBe(2);
        newer.Asked.Length.ShouldBe(1);
    }

    [Fact]
    public void A_pack_and_a_folder_of_loose_files_hand_a_script_on_as_the_same_text()
    {
        // A mark, a letter of two bytes and CRLF: what a rewrite of the text would lose.
        const char TwoBytes = (char)0xE4;
        byte[] saved =
            [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes($"print(\"{TwoBytes}\")\r\n")];

        using var project = new TempProject();
        project.WriteAsset(Door, saved);

        CookResult cooked = Cook(project, new FakeScriptCheck());
        var pack = project.Track(new PackSource(NullLogger.Instance, cooked.OutputPath.ShouldNotBeNull()));
        var loose = new LooseFileSource(NullLogger.Instance, project.Layout.AssetsPath);

        byte[] fromPack = new ContentScriptSource(pack).Read(Door).ShouldNotBeNull();
        byte[] fromFolder = new ContentScriptSource(loose).Read(Door).ShouldNotBeNull();

        fromPack.ShouldBe(saved);
        fromFolder.ShouldBe(saved);

        // What the pack holds is not the text itself.
        pack.TryOpen(Door, out ContentBlob? entry).ShouldBeTrue();
        using (entry)
            PackedScript.IsPacked(entry.Span).ShouldBeTrue();
    }

    [Fact]
    public void A_script_edited_after_the_cook_is_read_by_a_dev_boot_and_not_by_a_shipped_one()
    {
        using var project = new TempProject();
        project.WriteAsset(Door, "print(\"cooked\")\n");

        // Into the project's own cooked folder, where a boot looks.
        var settings = new CookSettings { UseCache = false, ScriptCheck = new FakeScriptCheck() };
        new CookSession(project.Layout, settings).Run(TestContext.Current.CancellationToken)
            .Succeeded.ShouldBeTrue();

        byte[] edited = project.WriteAsset(Door, "print(\"edited\")\n");

        // The loose file is plain text over a packed entry with a header.
        using (ProjectContentMount dev = ProjectContentMount.Open(
                   NullLogger.Instance, project.Layout, ContentMountProfile.Dev))
        {
            new ContentScriptSource(dev.Content).Read(Door).ShouldBe(edited);
        }

        using ProjectContentMount shipped = ProjectContentMount.Open(
            NullLogger.Instance, project.Layout, ContentMountProfile.Shipped);

        byte[] packed = new ContentScriptSource(shipped.Content).Read(Door).ShouldNotBeNull();
        Encoding.UTF8.GetString(packed).ShouldBe("print(\"cooked\")\n");
    }

    [Fact]
    public void The_verifier_passes_a_cooked_script_and_refuses_one_that_was_copied_in()
    {
        using var project = new TempProject();
        project.WriteAsset(Door, "print(1)\n");

        string cooked = Cook(project, new FakeScriptCheck()).OutputPath.ShouldNotBeNull();
        PackVerifier.Verify(cooked).Succeeded.ShouldBeTrue();

        // Written by hand: this cook packs no script it did not check.
        string copied = Path.Combine(project.Root, "copied.spack");
        var writer = new PackWriter();
        writer.Add(Door, PackEntryKind.Script, Encoding.UTF8.GetBytes("print(1)\n"));
        writer.WriteToFile(copied, TestContext.Current.CancellationToken);

        PackVerifyResult result = PackVerifier.Verify(copied);

        result.Succeeded.ShouldBeFalse();
        CookDiagnostic refused = result.Diagnostics.Single(d => d.IsError);
        refused.Id.ToString().ShouldBe("SC8003");
        refused.Message.ShouldContain(Door);
        CookGate.Verdict(CookDiagnosticCodes.ScriptFileUnreadable).ShouldBe(CookGateVerdict.Fatal);
    }

    // What a host that set nothing up cooks with.
    private static CookResult WithNoCheck(TempProject project) =>
        new CookSession(project.Layout, new CookSettings { UseCache = false }).Run();

    private static RuleContext Run(TempProject project, string path, IScriptSyntaxCheck check)
    {
        var context = new RuleContext(project.Layout.AssetsPath, path, CookProfile.Ship);
        new ScriptRule(check).Cook(context);
        return context;
    }

    private static CookResult Cook(
        TempProject project, IScriptSyntaxCheck check, bool cache = false, string label = "out") =>
        new CookSession(
                project.Layout,
                new CookSettings
                {
                    UseCache = cache,
                    ScriptCheck = check,
                    OutputPath = Path.Combine(project.Root, label),
                })
            .Run();
}
