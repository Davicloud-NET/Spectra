using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraEngine.Core.Projects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// A project cooked into a pack, mounted with nothing else, serving a texture,
/// a material and a shader.
/// </summary>
public class PackedBootTests
{
    private const string TexturePath = "Textures/wall.png";
    private const string MaterialPath = "Materials/wall.spectramat";
    private const string ShaderPath = "Shaders/Lit.spectrashade";
    private const string NotesPath = "Data/notes.txt";

    [Fact]
    public void A_shipped_boot_resolves_a_texture_a_material_and_a_shader_out_of_the_pack_alone()
    {
        using var project = new TempProject();
        WriteContent(project);
        CookInto(project);

        using ProjectContentMount mount = Open(project, ContentMountProfile.Shipped);

        // Nothing loose is mounted, so only the cooked .simage can fill the slot.
        mount.Content.Exists(TexturePath).ShouldBeFalse(
            "the pack carries the cooked image, and an authored PNG is never copied beside it");

        using AssetManager assets = Attach(project, mount);
        Material material = assets.LoadMaterial(MaterialPath);

        material.ShouldNotBe(assets.DefaultMaterial);
        material.TryGetTexture("uDiffuse", out _, out Texture? bound).ShouldBeTrue();

        // A slot that fell back would bind the Rgb8 placeholder.
        bound.Format.ShouldBe(TextureFormat.Bc7);

        ResolvedShader shader = BaseShaderResolver.ResolveBuiltIn(
            mount.Content, "Lit.spectrashade", GraphicsBackend.OpenGL, NullLogger.Instance);

        shader.Cooked.ShouldNotBeNull();
        shader.Source.ShouldBeNull("a cooked blob beats the source beside it");
        shader.WatchPath.ShouldBeNull("a packed shader has no file for a watcher to watch");

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_pack_serves_the_same_path_however_a_caller_spells_it()
    {
        using var project = new TempProject();
        WriteContent(project);
        CookInto(project);

        using ProjectContentMount mount = Open(project, ContentMountProfile.Shipped);

        mount.Content.Exists(NotesPath).ShouldBeTrue();
        mount.Content.Exists(@"Data\notes.txt").ShouldBeTrue();
        mount.Content.Exists("/Data/notes.txt").ShouldBeTrue();
        mount.Content.Exists("Data/absent.txt").ShouldBeFalse();
    }

    [Fact]
    public void A_shipped_boot_reports_hot_reload_off_and_says_why()
    {
        using var project = new TempProject();
        WriteContent(project);
        CookInto(project);

        var log = new CapturingLogger();
        using ProjectContentMount mount = Open(project, ContentMountProfile.Shipped, log);

        mount.HotReloadEnabled.ShouldBeFalse();
        mount.HotReloadDisabledReason.ShouldNotBeNullOrWhiteSpace();

        log.MessagesAt(LogLevel.Information)
            .ShouldContain(line => line.Contains("Hot reload OFF") && line.Contains("watcher"), log.Describe());

        // A pack has no watch path.
        mount.Content.TryGetWatchPath(NotesPath, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_dev_boot_lets_a_loose_file_shadow_the_packed_one_and_says_so()
    {
        using var project = new TempProject();
        WriteContent(project);
        CookInto(project);

        // Edited after the cook.
        project.WriteAsset(NotesPath, "edited\n");

        var log = new CapturingLogger();
        using ProjectContentMount mount = Open(project, ContentMountProfile.Dev, log);

        mount.HotReloadEnabled.ShouldBeTrue();
        mount.HotReloadDisabledReason.ShouldBeNull();
        Read(mount.Content, NotesPath).ShouldBe("edited\n");

        // Filtered by path: the material is shadowed too.
        MountShadowing shadowing = mount.Shadowings.Single(entry => entry.Path == NotesPath);
        shadowing.WinnerPriority.ShouldBe(PackMountBand.Loose);
        shadowing.ShadowedPriority.ShouldBe(PackMountBand.Base);

        log.MessagesAt(LogLevel.Information)
            .ShouldContain(line => line.Contains("Mount shadowing") && line.Contains(NotesPath), log.Describe());

        mount.Content.TryGetWatchPath(NotesPath, out _).ShouldBeTrue();
    }

    [Fact]
    public void A_shipped_boot_serves_the_cook_even_where_a_loose_file_disagrees()
    {
        using var project = new TempProject();
        WriteContent(project);
        CookInto(project);
        project.WriteAsset(NotesPath, "edited\n");

        using ProjectContentMount mount = Open(project, ContentMountProfile.Shipped);

        Read(mount.Content, NotesPath).ShouldBe("cooked\n");
        mount.Shadowings.ShouldBeEmpty();
    }

    [Fact]
    public void A_manifest_that_names_its_packs_mounts_exactly_those_in_order()
    {
        using var project = new TempProject();
        WriteContent(project);
        CookInto(project);

        // Not at the conventional path, so a resolver ignoring the manifest fails.
        string listed = Path.Combine(project.Root, "packs", "base.spack");
        Directory.CreateDirectory(Path.GetDirectoryName(listed)!);
        File.Copy(ProjectPacks.ConventionalPackPath(project.Layout), listed);

        project.Layout.Project.Packs.Add("packs/base.spack");

        ProjectPacks.Resolve(project.Layout).ShouldBe([Path.GetFullPath(listed)]);

        using ProjectContentMount mount = Open(project, ContentMountProfile.Shipped);
        mount.PackPaths.ShouldBe([Path.GetFullPath(listed)]);
        mount.Content.Exists(NotesPath).ShouldBeTrue();
    }

    [Fact]
    public void An_uncooked_project_is_refused_at_the_mount_and_told_how_to_cook()
    {
        using var project = new TempProject();
        WriteContent(project);

        Should.Throw<PackMountException>(() => Open(project, ContentMountProfile.Shipped))
            .Message.ShouldContain("scook cook");
    }

    private static void WriteContent(TempProject project)
    {
        project.WriteAsset(TexturePath, TempProject.Png(width: 8, height: 8, seed: 7));
        project.WriteAsset(
            MaterialPath,
            $"shader = Lit\ntexture uDiffuse = {TexturePath}, nearest, clamp\n");

        project.WriteAsset(ShaderPath, BaseShaders.Lit);
        project.WriteAsset(NotesPath, "cooked\n");
    }

    // OpenGL only: that is what the fake renderer reports.
    private static void CookInto(TempProject project)
    {
        CookResult result = new CookSession(
            project.Layout,
            new CookSettings { UseCache = false, Targets = [GraphicsBackend.OpenGL] }).Run();

        result.Succeeded.ShouldBeTrue(
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    private static ProjectContentMount Open(
        TempProject project, ContentMountProfile profile, ILogger? logger = null) =>
        ProjectContentMount.Open(logger ?? NullLogger.Instance, project.Layout, profile);

    // The content root stays Assets/ even with nothing loose mounted. Model
    // imports and SourcePath resolve against it; the stack supplies the bytes.
    private static AssetManager Attach(TempProject project, ProjectContentMount mount)
    {
        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance,
            project.Layout.AssetsPath,
            mount.Content,
            mount.HotReloadEnabled);

        assets.AttachRenderer(new FakeRenderer());
        return assets;
    }

    private static string Read(IContentSource content, string path)
    {
        content.TryOpen(path, out ContentBlob? blob).ShouldBeTrue();
        using (blob!)
            return Encoding.UTF8.GetString(blob.Span);
    }
}
