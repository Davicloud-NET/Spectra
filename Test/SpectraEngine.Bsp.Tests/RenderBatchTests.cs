using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Collapsing repeated draws into instanced batches.</summary>
public sealed class RenderBatchTests
{
    // Batching compares meshes and materials by reference.
    private static Mesh NewMesh() => new StubMesh();
    private static Material NewMaterial() => new(null!);

    private sealed class StubMesh : Mesh
    {
        public override void Draw() { }
        public override void DrawInstanced(InstanceBuffer instances, int instanceCount, int firstInstance = 0) { }
        public override void Dispose() { }
    }

    private static RenderView ViewOf(params RenderItem[] items)
    {
        var view = new RenderView();
        foreach (RenderItem item in items)
            view.Add(item);
        view.BuildBatches();
        return view;
    }

    private static RenderItem At(Mesh mesh, Material? material, float x) =>
        new(mesh, material, Matrix4x4.CreateTranslation(x, 0f, 0f));

    private static RenderItem[] Repeat(Mesh mesh, Material? material, int count, int from = 0) =>
        [.. Enumerable.Range(from, count).Select(i => At(mesh, material, i))];

    [Fact]
    public void Enough_copies_of_one_mesh_become_one_batch()
    {
        Mesh mesh = NewMesh();
        Material material = NewMaterial();

        RenderView view = ViewOf(Repeat(mesh, material, 6));

        view.Batches.Count.ShouldBe(1);
        view.Batches[0].Count.ShouldBe(6);
        view.Batches[0].Mesh.ShouldBeSameAs(mesh);
        view.Batches[0].Material.ShouldBeSameAs(material);
        view.SingleItems.ShouldBeEmpty();
    }

    [Fact]
    public void Too_few_copies_stay_individual_draws()
    {
        // An instanced draw binds a second buffer and, on D3D12, another pipeline.
        // Below the threshold that costs more than it saves.
        Mesh mesh = NewMesh();
        Material material = NewMaterial();

        RenderView view = ViewOf(Repeat(mesh, material, RenderView.MinimumBatchSize - 1));

        view.Batches.ShouldBeEmpty();
        view.SingleItems.Count.ShouldBe(RenderView.MinimumBatchSize - 1);
    }

    [Fact]
    public void Every_item_lands_in_exactly_one_of_the_two()
    {
        Mesh repeated = NewMesh();
        Mesh lonely = NewMesh();
        Material material = NewMaterial();

        RenderItem[] items = [.. Repeat(repeated, material, 5), At(lonely, material, 99)];
        RenderView view = ViewOf(items);

        int batched = view.Batches.Sum(b => b.Count);
        (batched + view.SingleItems.Count).ShouldBe(items.Length);
        batched.ShouldBe(5);
        view.SingleItems.Single().Mesh.ShouldBeSameAs(lonely);
    }

    [Fact]
    public void The_same_mesh_with_different_materials_does_not_batch_together()
    {
        Mesh mesh = NewMesh();
        Material a = NewMaterial();
        Material b = NewMaterial();

        RenderView view = ViewOf([.. Repeat(mesh, a, 5), .. Repeat(mesh, b, 5)]);

        view.Batches.Count.ShouldBe(2);
        view.Batches[0].Material.ShouldBeSameAs(a);
        view.Batches[1].Material.ShouldBeSameAs(b);
    }

    [Fact]
    public void Structurally_similar_but_distinct_meshes_do_not_batch_together()
    {
        Material material = NewMaterial();
        RenderItem[] items = [.. Enumerable.Range(0, 8).Select(i => At(NewMesh(), material, i))];

        RenderView view = ViewOf(items);

        view.Batches.ShouldBeEmpty();
        view.SingleItems.Count.ShouldBe(8);
    }

    [Fact]
    public void Copies_scattered_through_the_list_still_batch()
    {
        // The draw list comes in spatial-index order, so copies of one prop
        // arrive interleaved with their neighbours.
        Mesh prop = NewMesh();
        Material material = NewMaterial();

        var items = new List<RenderItem>();
        for (int i = 0; i < 5; i++)
        {
            items.Add(At(prop, material, i));
            items.Add(At(NewMesh(), material, i));
        }

        RenderView view = ViewOf([.. items]);

        view.Batches.Count.ShouldBe(1);
        view.Batches[0].Count.ShouldBe(5);
        view.SingleItems.Count.ShouldBe(5);
    }

    [Fact]
    public void A_batch_owns_a_contiguous_run_of_transforms()
    {
        // A draw names its instances as a range.
        Mesh first = NewMesh();
        Mesh second = NewMesh();
        Material material = NewMaterial();

        var items = new List<RenderItem>();
        for (int i = 0; i < 4; i++)
        {
            items.Add(At(first, material, i));
            items.Add(At(second, material, 100 + i));
        }

        RenderView view = ViewOf([.. items]);
        ReadOnlySpan<Matrix4x4> transforms = view.InstanceTransforms;

        view.Batches.Count.ShouldBe(2);
        transforms.Length.ShouldBe(8);

        RenderBatch a = view.Batches[0];
        RenderBatch b = view.Batches[1];
        a.Offset.ShouldBe(0);
        b.Offset.ShouldBe(4);

        for (int i = 0; i < 4; i++)
        {
            transforms[a.Offset + i].Translation.X.ShouldBe(i);
            transforms[b.Offset + i].Translation.X.ShouldBe(100 + i);
        }
    }

    [Fact]
    public void Transforms_within_a_batch_keep_emission_order()
    {
        // Same picture either way, but an unchanged scene should give
        // byte-identical instance data.
        Mesh mesh = NewMesh();
        Material material = NewMaterial();

        RenderView view = ViewOf(Repeat(mesh, material, 6));

        for (int i = 0; i < 6; i++)
            view.InstanceTransforms[i].Translation.X.ShouldBe(i);
    }

    [Fact]
    public void Batch_order_follows_first_appearance()
    {
        // Dictionary order is unspecified and would make the view unstable.
        Mesh second = NewMesh();
        Mesh first = NewMesh();
        Material material = NewMaterial();

        RenderView view = ViewOf([At(first, material, 0), .. Repeat(second, material, 4), .. Repeat(first, material, 4, 1)]);

        view.Batches.Count.ShouldBe(2);
        view.Batches[0].Mesh.ShouldBeSameAs(first, "it appeared at index 0");
        view.Batches[1].Mesh.ShouldBeSameAs(second);
    }

    [Fact]
    public void Rebuilding_an_unchanged_view_gives_an_identical_partition()
    {
        Mesh mesh = NewMesh();
        Material material = NewMaterial();
        RenderItem[] items = [.. Repeat(mesh, material, 5), At(NewMesh(), material, 50)];

        var view = new RenderView();
        foreach (RenderItem item in items) view.Add(item);
        view.BuildBatches();
        RenderBatch[] firstPass = [.. view.Batches];
        Matrix4x4[] firstTransforms = view.InstanceTransforms.ToArray();

        view.BuildBatches();

        view.Batches.ShouldBe(firstPass);
        view.InstanceTransforms.ToArray().ShouldBe(firstTransforms);
    }

    [Fact]
    public void Clearing_the_view_drops_the_partition_with_it()
    {
        // A stale partition would draw last frame's batches against meshes
        // that may already be destroyed.
        Mesh mesh = NewMesh();
        RenderView view = ViewOf(Repeat(mesh, NewMaterial(), 6));
        view.Batches.ShouldNotBeEmpty();

        view.Clear();

        view.Batches.ShouldBeEmpty();
        view.SingleItems.ShouldBeEmpty();
        view.InstanceTransforms.Length.ShouldBe(0);
        view.DrawsSaved.ShouldBe(0);
    }

    [Fact]
    public void An_empty_view_batches_nothing_and_does_not_throw()
    {
        RenderView view = ViewOf();

        view.Batches.ShouldBeEmpty();
        view.SingleItems.ShouldBeEmpty();
        view.DrawsSaved.ShouldBe(0);
    }

    [Fact]
    public void Draws_saved_counts_what_the_batching_removed()
    {
        Mesh mesh = NewMesh();
        Material material = NewMaterial();

        ViewOf(Repeat(mesh, material, 6)).DrawsSaved.ShouldBe(5);
    }

    [Fact]
    public void Batching_allocates_nothing_once_it_has_run_once()
    {
        Mesh mesh = NewMesh();
        Material material = NewMaterial();
        var view = new RenderView();
        foreach (RenderItem item in Repeat(mesh, material, 64))
            view.Add(item);

        for (int i = 0; i < 8; i++)
            view.BuildBatches();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
            view.BuildBatches();
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0);
    }
}
