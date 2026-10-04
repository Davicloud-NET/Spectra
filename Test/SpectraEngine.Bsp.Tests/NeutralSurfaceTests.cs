using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using System.Numerics;
using static SpectraEngine.Bsp.Tests.SpatialTestHelpers;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A face naming no material draws neutral grey; a face naming a missing one
/// still draws the magenta placeholder, warns and is counted.
/// </summary>
public sealed class NeutralSurfaceTests
{
    [Fact]
    public void An_editor_style_scene_draws_unnamed_faces_neutral_not_magenta()
    {
        var logger = new CapturingLogger();
        var renderer = new FakeRenderer();
        var assets = new AssetManager(logger, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);

        // How the editor boots: baseplate, asset manager, no StaticWorldMaterial.
        var scene = new Scene("Fresh") { Assets = assets };
        SceneManager.PopulateBaseplate(scene);
        scene.StaticWorldMaterial.ShouldBeNull();

        scene.RebuildStaticWorld(renderer);

        scene.StaticWorldChunkMeshes.Count.ShouldBeGreaterThan(0);
        foreach (StaticWorldChunkMesh chunk in scene.StaticWorldChunkMeshes)
        {
            foreach (StaticWorldSubmesh submesh in chunk.Submeshes)
            {
                submesh.Material.ShouldBeSameAs(assets.NeutralMaterial);
                submesh.Material.ShouldNotBeSameAs(assets.DefaultMaterial);
            }
        }

        assets.NeutralMaterial.TryGetTexture("uDiffuse", out _, out Texture? bound).ShouldBeTrue();
        bound.ShouldNotBeSameAs(assets.PlaceholderTexture);

        logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty(logger.Describe());
        logger.MessagesAt(LogLevel.Error).ShouldBeEmpty(logger.Describe());

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void The_neutral_surface_is_the_grey_the_development_grid_names()
    {
        var assets = new AssetManager(NullLogger, ContentRoot.Path, hotReloadEnabled: false);

        // #8C8C99, stored linear like a parsed colour directive.
        Vector3 expected = ColorSpace.SrgbToLinear(new Vector3(140f / 255f, 140f / 255f, 153f / 255f));

        assets.NeutralMaterial.TryGetVector3("uBaseColor", out Vector3 tint).ShouldBeTrue();
        tint.X.ShouldBe(expected.X, 1e-6f);
        tint.Y.ShouldBe(expected.Y, 1e-6f);
        tint.Z.ShouldBe(expected.Z, 1e-6f);
    }

    [Fact]
    public void An_explicit_world_material_still_wins_over_the_neutral_one()
    {
        var renderer = new FakeRenderer();
        var assets = new AssetManager(NullLogger, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);

        var scene = new Scene("Test") { Assets = assets, StaticWorldMaterial = NoopMaterial };
        AddUnnamedBrush(scene, new Vector3(10f, 10f, 10f));

        scene.RebuildStaticWorld(renderer);

        scene.StaticWorldChunkMeshes.ShouldHaveSingleItem()
            .Submeshes.ShouldHaveSingleItem()
            .Material.ShouldBeSameAs(NoopMaterial);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_scene_with_no_asset_manager_still_draws_nothing_rather_than_throwing()
    {
        var renderer = new FakeRenderer();

        // Headless: no manager, so a face resolves to null and the swap skips it.
        var scene = new Scene("Test");
        AddUnnamedBrush(scene, new Vector3(10f, 10f, 10f));

        scene.RebuildStaticWorld(renderer);

        scene.StaticWorldChunkMeshes.ShouldHaveSingleItem()
            .Submeshes.ShouldHaveSingleItem()
            .Material.ShouldBeNull();
    }

    [Fact]
    public void Neutral_material_is_never_null_and_is_never_the_default_material()
    {
        var renderer = new FakeRenderer();
        var assets = new AssetManager(NullLogger, ContentRoot.Path, hotReloadEnabled: false);

        assets.NeutralMaterial.ShouldNotBeNull();
        assets.NeutralMaterial.Name.ShouldBe(AssetManager.NeutralMaterialName);
        assets.NeutralMaterial.ShouldNotBeSameAs(assets.DefaultMaterial);
        assets.NeutralMaterial.Shader.ShouldBeNull();

        assets.AttachRenderer(renderer);
        assets.NeutralMaterial.Shader.ShouldNotBeNull();
        assets.NeutralMaterial.TryGetTexture("uDiffuse", out _, out _).ShouldBeTrue();

        // Callers still hold the instance after release; it must not point
        // at a destroyed texture.
        assets.ReleaseGraphicsResources();
        assets.NeutralMaterial.ShouldNotBeNull();
        assets.NeutralMaterial.Shader.ShouldBeNull();
        assets.NeutralMaterial.TryGetTexture("uDiffuse", out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_named_but_missing_material_still_wears_the_placeholder_and_is_counted()
    {
        var logger = new CapturingLogger();
        var renderer = new FakeRenderer();
        var assets = new AssetManager(logger, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);

        MaterialRef missing = MaterialRegistry.Intern($"Materials/nope_{Guid.NewGuid():N}.spectramat");
        var scene = new Scene("Test") { Assets = assets };
        AddUnnamedBrush(scene, new Vector3(10f, 10f, 10f));
        AddNamedBrush(scene, new Vector3(80f, 16f, 16f), missing);

        scene.RebuildStaticWorld(renderer);

        StaticWorldChunkMesh unnamed =
            scene.StaticWorldChunkMeshes.Single(c => c.Coord == new ChunkCoord(0, 0, 0));
        unnamed.Submeshes.ShouldHaveSingleItem().Material.ShouldBeSameAs(assets.NeutralMaterial);

        StaticWorldChunkMesh named =
            scene.StaticWorldChunkMeshes.Single(c => c.Coord == new ChunkCoord(2, 0, 0));
        named.Submeshes.ShouldHaveSingleItem().Material.ShouldBeSameAs(assets.DefaultMaterial);

        assets.PlaceholderBoundCount.ShouldBe(1);
        logger.MessagesAt(LogLevel.Warning).ShouldNotBeEmpty(logger.Describe());

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_healthy_scene_counts_nothing()
    {
        var renderer = new FakeRenderer();
        var assets = new AssetManager(NullLogger, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);

        assets.PlaceholderBoundCount.ShouldBe(0);
        assets.LoadMaterial("Materials/dev_grid.spectramat");
        assets.PlaceholderBoundCount.ShouldBe(0);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_material_naming_a_missing_texture_counts_one_however_often_it_is_loaded()
    {
        var logger = new CapturingLogger();
        string root = CreateTempContentRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Materials", "gone.spectramat"),
                "texture uDiffuse = Textures/not_here.png");

            var renderer = new FakeRenderer();
            var assets = new AssetManager(logger, root, hotReloadEnabled: false);
            assets.AttachRenderer(renderer);

            assets.LoadMaterial("Materials/gone.spectramat");
            assets.PlaceholderBoundCount.ShouldBe(1);

            assets.LoadMaterial("Materials/gone.spectramat");
            assets.PlaceholderBoundCount.ShouldBe(1);

            assets.ReleaseGraphicsResources();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_material_file_counts_once_and_not_twice()
    {
        var logger = new CapturingLogger();
        var renderer = new FakeRenderer();
        var assets = new AssetManager(logger, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);

        // The fallback material's own slot also holds the checker; that must
        // not count as a second failure.
        assets.ResolveMaterial(MaterialRegistry.Intern($"Materials/absent_{Guid.NewGuid():N}.spectramat"));

        assets.PlaceholderBoundCount.ShouldBe(1);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_load_still_in_flight_is_not_counted_and_a_failed_one_is()
    {
        var logger = new CapturingLogger();
        string root = CreateTempContentRoot();
        try
        {
            var renderer = new FakeRenderer();
            var assets = new AssetManager(logger, root, hotReloadEnabled: false);
            assets.AttachRenderer(renderer);

            // A pending load is bound to the placeholder but is not a failure yet.
            TextureAsset pending = assets.RequestTexture("Textures/absent.png");
            pending.IsPlaceholder.ShouldBeTrue();
            assets.PlaceholderBoundCount.ShouldBe(0);

            PumpUntil(assets, () => pending.LoadFailed);
            assets.PlaceholderBoundCount.ShouldBe(1);

            assets.ReleaseGraphicsResources();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static readonly Microsoft.Extensions.Logging.Abstractions.NullLogger NullLogger =
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private static SceneNode AddUnnamedBrush(Scene scene, Vector3 position) =>
        AddNamedBrush(scene, position, MaterialRef.Default);

    private static SceneNode AddNamedBrush(Scene scene, Vector3 position, MaterialRef material)
    {
        SceneNode node = scene.Root.CreateChild($"brush{position.X}");
        node.LocalPosition = position;
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f), material);
        return node;
    }

    private static string CreateTempContentRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "SpectraNeutralTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Materials"));
        Directory.CreateDirectory(Path.Combine(root, "Textures"));
        return root;
    }

    private static void PumpUntil(AssetManager assets, Func<bool> condition)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            assets.PumpPendingUploads();
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("Timed out waiting for a decode to report failure.");
            Thread.Sleep(1);
        }
    }
}
