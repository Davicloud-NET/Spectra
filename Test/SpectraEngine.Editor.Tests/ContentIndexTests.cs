using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Editor.Shell;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The index behind both content views: what one walk finds, and what one
/// filesystem change does to it.
/// </summary>
/// <remarks>
/// <b>ONE reader, two views, and this is the test that says the second one is
/// not a second reader.</b> Enumerating the directory for the folder view and
/// indexing for the search would disagree the first time somebody renamed a
/// file: the folder shows the new name because it just listed, the search shows
/// the old one because nothing told it, and neither reports a problem.
///
/// The watcher itself is not tested. It needs a real filesystem, real timing and
/// a real dispatcher; what a test can hold is that a create appears, a rename
/// moves rather than duplicating, and a delete disappears - which is why
/// <c>ApplyChange</c> is public, the same reason <c>AssetManager</c>'s own
/// change notification is.
/// </remarks>
public sealed class ContentIndexTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spectra-index-" + Guid.NewGuid().ToString("N"));

    public ContentIndexTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Materials"));
        Directory.CreateDirectory(Path.Combine(_root, "Textures", "dev"));

        Write("Materials/wall.spectramat");
        Write("Textures/brick.png");
        Write("Textures/dev/grid.png");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Write(string relative)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    private string Full(string relative) =>
        Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    // The real walk, awaited: seeding by hand would test the seeding. Awaiting
    // is also what makes the change tests deterministic, since a walk landing
    // underneath one would replace the very list it is asserting about.
    private static async System.Threading.Tasks.Task<ContentIndex> WalkedAsync(string root)
    {
        var index = new ContentIndex(NullLogger.Instance);
        index.SetRoot(root);
        await index.Walking;
        return index;
    }

    private System.Threading.Tasks.Task<ContentIndex> WalkedAsync() => WalkedAsync(_root);

    [Fact]
    public async System.Threading.Tasks.Task An_entry_carries_the_content_path_and_the_kind()
    {
        ContentIndex index = await WalkedAsync();

        ContentIndexEntry entry = Find(index, "wall.spectramat");

        entry.ContentPath.ShouldBe("Materials/wall.spectramat");
        entry.Kind.ShouldBe(ContentKind.Material);
        entry.Bytes.ShouldBe(1);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_folder_is_indexed_with_no_content_path()
    {
        ContentIndex index = await WalkedAsync();

        ContentIndexEntry folder = Find(index, "Materials");

        folder.Kind.ShouldBe(ContentKind.Folder);

        // A folder is navigation rather than content: it has no identity in the
        // engine, so carrying a path for it would be inventing one.
        folder.ContentPath.ShouldBe("");
        folder.Bytes.ShouldBe(-1);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_folder_listing_is_folders_first_then_names()
    {
        ContentIndex index = await WalkedAsync();

        List<ContentIndexEntry> rows = index.InFolder(Full("Textures"));

        rows.Count.ShouldBe(2);
        rows[0].Name.ShouldBe("dev");
        rows[1].Name.ShouldBe("brick.png");
    }

    [Fact]
    public async System.Threading.Tasks.Task A_listing_shows_one_folder_only_and_not_its_children()
    {
        ContentIndex index = await WalkedAsync();

        foreach (ContentIndexEntry row in index.InFolder(_root))
            row.Name.ShouldNotBe("brick.png");
    }

    [Fact]
    public async System.Threading.Tasks.Task A_created_file_appears()
    {
        ContentIndex index = await WalkedAsync();
        Write("Materials/floor.spectramat");

        index.ApplyChange(WatcherChangeTypes.Created, Full("Materials/floor.spectramat"), null);

        Find(index, "floor.spectramat").ContentPath.ShouldBe("Materials/floor.spectramat");
    }

    [Fact]
    public async System.Threading.Tasks.Task A_renamed_file_moves_rather_than_duplicating()
    {
        ContentIndex index = await WalkedAsync();

        File.Move(Full("Materials/wall.spectramat"), Full("Materials/wall2.spectramat"));
        index.ApplyChange(
            WatcherChangeTypes.Renamed,
            Full("Materials/wall2.spectramat"),
            Full("Materials/wall.spectramat"));

        // Both halves: the new name is there AND the old one is gone. An index
        // that only added would show a file that does not exist, which is the
        // exact failure a second reader produces.
        Find(index, "wall2.spectramat").ShouldNotBeNull();
        Missing(index, "wall.spectramat");
    }

    [Fact]
    public async System.Threading.Tasks.Task A_removed_file_disappears()
    {
        ContentIndex index = await WalkedAsync();

        File.Delete(Full("Textures/brick.png"));
        index.ApplyChange(WatcherChangeTypes.Deleted, Full("Textures/brick.png"), null);

        Missing(index, "brick.png");
    }

    [Fact]
    public async System.Threading.Tasks.Task A_change_to_a_file_already_indexed_replaces_it_rather_than_adding()
    {
        ContentIndex index = await WalkedAsync();
        int before = index.Count;

        File.WriteAllText(Full("Textures/brick.png"), "much longer content");
        index.ApplyChange(WatcherChangeTypes.Changed, Full("Textures/brick.png"), null);

        index.Count.ShouldBe(before);
        Find(index, "brick.png").Bytes.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_file_outside_the_root_cannot_be_named_and_is_not_indexed()
    {
        ContentIndex index = await WalkedAsync();
        int before = index.Count;

        string outside = Path.Combine(Path.GetTempPath(), "spectra-outside.png");
        File.WriteAllText(outside, "x");

        try
        {
            index.ApplyChange(WatcherChangeTypes.Created, outside, null);
            index.Count.ShouldBe(before);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task No_root_indexes_nothing()
    {
        var index = new ContentIndex(NullLogger.Instance);
        index.SetRoot(null);
        await index.Walking;

        index.Count.ShouldBe(0);
        index.ApplyChange(WatcherChangeTypes.Created, Full("Textures/brick.png"), null);
        index.Count.ShouldBe(0);
    }

    private static ContentIndexEntry Find(ContentIndex index, string name)
    {
        foreach (ContentIndexEntry entry in index.Entries)
        {
            if (string.Equals(entry.Name, name, StringComparison.Ordinal)) return entry;
        }

        throw new Xunit.Sdk.XunitException($"No entry named '{name}'.");
    }

    private static void Missing(ContentIndex index, string name)
    {
        foreach (ContentIndexEntry entry in index.Entries)
            entry.Name.ShouldNotBe(name);
    }
}
