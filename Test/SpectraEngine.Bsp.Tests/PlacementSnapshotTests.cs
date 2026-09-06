using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

public sealed class PlacementSnapshotTests
{
    [Fact]
    public void Structural_patches_are_byte_identical_to_dense_authored_compiles()
    {
        var journal = new PlacementJournal();
        var box = Brush.CreateBox(new Vector3(-2), new Vector3(2));
        var ids = new List<Guid>();
        for (int i = 0; i < 5; i++)
        {
            var id = Guid.NewGuid();
            journal.Set(id, new(box, Matrix4x4.CreateTranslation(i == 0 ? 1000 : i * 2, 0, 0)), ids.LastOrDefault());
            ids.Add(id);
        }
        CsgWorld previous = CsgWorld.Build(journal.Capture());
        var remote = ChunkGrid.OwnerCell(new BrushPlacement(box, Matrix4x4.CreateTranslation(1000, 0, 0)));
        for (int edit = 0; edit < 48; edit++)
        {
            switch (edit % 4)
            {
                case 0:
                    var added = Guid.NewGuid();
                    journal.Set(added, new(box.WithOperation(edit % 8 == 0 ? BrushOperation.Subtractive : BrushOperation.Additive),
                        Matrix4x4.CreateTranslation(3, .5f, 0)), ids[1]);
                    ids.Insert(2, added);
                    break;
                case 1:
                    journal.Set(ids[3], new(box, Matrix4x4.CreateTranslation(3 + edit % 3, 1, 0)), null);
                    break;
                case 2:
                    journal.Set(ids[2], new(box, Matrix4x4.CreateTranslation(3, .5f, 0)), ids[^1], reorder: true);
                    var moved = ids[2]; ids.RemoveAt(2); ids.Add(moved);
                    break;
                case 3:
                    journal.Remove(ids[^1]); ids.RemoveAt(ids.Count - 1);
                    break;
            }
            var snapshot = journal.Capture();
            var dirty = new HashSet<ChunkCoord>();
            foreach (var change in snapshot.Changes)
            {
                if (change.Before is { } before) dirty.UnionWith(ChunkGrid.ComputeFootprint(before.Placement));
                if (change.After is { } after) dirty.UnionWith(ChunkGrid.ComputeFootprint(after.Placement));
            }
            var patched = CsgWorld.Build(snapshot, dirty.Order().ToArray(), previous);
            var full = CsgWorld.Build(snapshot.ToArray());
            patched.PatchBaseId.ShouldBe(previous.Id, $"edit {edit} must patch");
            var expected = full.BuildMesh(); var actual = patched.BuildMesh();
            actual.Vertices.SequenceEqual(expected.Vertices).ShouldBeTrue($"edit {edit}: vertex bytes differ");
            actual.Indices.SequenceEqual(expected.Indices).ShouldBeTrue($"edit {edit}: index bytes differ");
            patched.Placements.ToArray().ShouldBe(snapshot.ToArray());
            foreach (var chunk in patched.Chunks.OrderedChunks)
                foreach (int index in chunk.ResidentBrushIndices)
                    index.ShouldBeInRange(0, snapshot.Count - 1);
            previous.StorageChunks.TryGet(remote, out var oldRemote).ShouldBeTrue();
            patched.StorageChunks.TryGet(remote, out var newRemote).ShouldBeTrue();
            newRemote.ShouldBeSameAs(oldRemote);
            previous = patched;
        }
    }

    [Fact]
    public void Insert_remove_and_reorder_preserve_old_snapshots_and_surviving_slots()
    {
        var brush = Brush.CreateBox(new Vector3(-1), new Vector3(1));
        var journal = new PlacementJournal();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        journal.Set(a, new(brush, Matrix4x4.CreateTranslation(1, 0, 0)));
        journal.Set(b, new(brush, Matrix4x4.CreateTranslation(2, 0, 0)), a);
        var original = journal.Capture();
        journal.Set(c, new(brush, Matrix4x4.CreateTranslation(3, 0, 0)), a);
        var inserted = journal.Capture();
        inserted.Select(p => p.Transform.Translation.X).ShouldBe(new[] { 1f, 3f, 2f });
        inserted.EntryAt(2).Slot.ShouldBe(original.EntryAt(1).Slot);
        inserted.Changes.Count.ShouldBe(1);
        inserted.ParentId.ShouldBe(original.Id);
        journal.Remove(a);
        journal.Set(b, inserted[2], null, reorder: true);
        var reordered = journal.Capture();
        reordered.Select(p => p.Transform.Translation.X).ShouldBe(new[] { 2f, 3f });
        original.Select(p => p.Transform.Translation.X).ShouldBe(new[] { 1f, 2f });
        reordered.DenseIndex(reordered.EntryAt(0).Slot).ShouldBe(0);
        reordered.DenseIndex(reordered.EntryAt(1).Slot).ShouldBe(1);
        reordered.Slots[original.EntryAt(0).Slot].ShouldBeNull();
    }

    [Fact]
    public void Repeated_insertions_and_deletions_match_authored_order_across_page_growth()
    {
        var brush = Brush.CreateBox(new Vector3(-1), new Vector3(1));
        var journal = new PlacementJournal();
        var expected = new List<Guid>();
        var random = new Random(1703);
        for (int i = 0; i < 2200; i++)
        {
            int index = i < 150 ? 0 : random.Next(expected.Count + 1);
            Guid id = Guid.NewGuid();
            journal.Set(id, new(brush, Matrix4x4.CreateTranslation(i, 0, 0)), index == 0 ? null : expected[index - 1]);
            expected.Insert(index, id);
        }
        var before = journal.Capture();
        for (int i = 0; i < 700; i++)
        {
            int index = random.Next(expected.Count);
            journal.Remove(expected[index]);
            expected.RemoveAt(index);
        }
        var after = journal.Capture();
        after.Count.ShouldBe(expected.Count);
        before.Count.ShouldBe(2200);
        for (int i = 0; i < expected.Count; i++)
        {
            after.EntryAt(i).Identity.ShouldBe(expected[i]);
            after.DenseIndex(after.EntryAt(i).Slot).ShouldBe(i);
        }
        after.ToDense(out var slots).ShouldBe(after.ToArray());
        slots.Length.ShouldBe(after.Count);
    }
}
