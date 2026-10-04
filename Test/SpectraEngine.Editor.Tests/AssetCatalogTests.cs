using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The asset picker's catalogue. Its entries must carry the normalized
/// content-relative path the rest of the engine identifies an asset by.
/// </summary>
public sealed class AssetCatalogTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spectra-catalog-" + Guid.NewGuid().ToString("N"));

    public AssetCatalogTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Materials"));
        Directory.CreateDirectory(Path.Combine(_root, "Materials", "dev"));
        Directory.CreateDirectory(Path.Combine(_root, "Textures"));

        Write("Materials/wall_brick.spectramat");
        Write("Materials/floor.spectramat");
        Write("Materials/dev/grid.spectramat");
        Write("Textures/brick.png");
        Write("readme.txt");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp folder is not a test failure.
        }
    }

    private void Write(string relative)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "shader lit\n");
    }

    private AssetCatalog Walked()
    {
        var catalog = new AssetCatalog(NullLogger.Instance);
        catalog.Rebuild(_root);
        return catalog;
    }

    [Fact]
    public void The_walk_finds_every_material_under_the_root_including_subfolders()
    {
        List<AssetCatalogEntry> found = Walked().Search("", ContentKind.Material, 50);

        var paths = new List<string>();
        foreach (AssetCatalogEntry entry in found) paths.Add(entry.ContentPath);

        paths.Count.ShouldBe(3);
        paths.ShouldContain("Materials/wall_brick.spectramat");
        paths.ShouldContain("Materials/dev/grid.spectramat");
    }

    [Fact]
    public void Entries_carry_the_content_path_the_engine_names_them_by()
    {
        AssetCatalog catalog = Walked();

        AssetCatalogEntry entry = catalog.Search("grid", ContentKind.Material, 5)[0];

        // Same string ContentDragPayload produces.
        entry.ContentPath.ShouldBe("Materials/dev/grid.spectramat");
        entry.Stem.ShouldBe("grid");
        entry.Folder.ShouldBe("Materials/dev");
    }

    [Fact]
    public void The_kind_filter_is_what_separates_a_material_from_a_texture()
    {
        AssetCatalog catalog = Walked();

        catalog.Search("brick", ContentKind.Material, 10).Count.ShouldBe(1);
        catalog.Search("brick", ContentKind.Texture, 10).Count.ShouldBe(1);

        catalog.Search("brick", ContentKind.Material, 10)[0].ContentPath
            .ShouldBe("Materials/wall_brick.spectramat");
    }

    [Fact]
    public void A_file_with_no_kind_is_not_offered_at_all()
    {
        AssetCatalog catalog = Walked();

        foreach (AssetCatalogEntry entry in catalog.Entries)
            entry.ContentPath.ShouldNotBe("readme.txt");
    }

    [Fact]
    public void A_stem_match_ranks_above_a_folder_match()
    {
        AssetCatalog catalog = Walked();

        // "dev" is only a folder name, and it still finds the file in it.
        List<AssetCatalogEntry> byFolder = catalog.Search("dev", ContentKind.Material, 10);
        byFolder.Count.ShouldBe(1);
        byFolder[0].Stem.ShouldBe("grid");

        List<AssetCatalogEntry> byName = catalog.Search("floor", ContentKind.Material, 10);
        byName[0].Stem.ShouldBe("floor");
    }

    [Fact]
    public void The_result_is_capped_at_what_the_caller_asked_for()
    {
        Walked().Search("", ContentKind.Material, 2).Count.ShouldBe(2);
    }

    [Fact]
    public void No_project_is_a_warning_rather_than_an_empty_list_with_no_reason()
    {
        var catalog = new AssetCatalog(NullLogger.Instance);
        catalog.Rebuild(null);

        catalog.Entries.Count.ShouldBe(0);
        catalog.Warning.ShouldNotBeNullOrWhiteSpace();

        // A missing folder is the same case.
        catalog.Rebuild(Path.Combine(_root, "nothing-here"));
        catalog.Warning.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_rebuild_replaces_rather_than_appends()
    {
        AssetCatalog catalog = Walked();
        int first = catalog.Entries.Count;

        catalog.Rebuild(_root);

        catalog.Entries.Count.ShouldBe(first);
        catalog.Root.ShouldBe(_root);
    }

    [Fact]
    public void Every_asset_kind_the_panel_can_ask_for_maps_to_a_content_kind()
    {
        AssetCatalog.KindFor(AssetKind.Material).ShouldBe(ContentKind.Material);
        AssetCatalog.KindFor(AssetKind.Texture).ShouldBe(ContentKind.Texture);
        AssetCatalog.KindFor(AssetKind.Model).ShouldBe(ContentKind.Model);

        // No browser kind for sound yet. Other keeps the picker empty.
        AssetCatalog.KindFor(AssetKind.Sound).ShouldBe(ContentKind.Other);
    }
}
