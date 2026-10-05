using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Rules;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// Cooks each corpus map, loads it, and checks what the runtime received is
/// bit-identical to a fresh cache-free compile of the same source.
/// </summary>
[Trait("Suite", "Determinism")]
public class BakeOracleTests
{
    [Theory]
    [InlineData(BakeCorpus.FlushCoplanarRoom)]
    [InlineData(BakeCorpus.Cavities)]
    [InlineData(BakeCorpus.Palette)]
    [InlineData(BakeCorpus.Sprawl)]
    public void The_loaded_geometry_is_bit_identical_to_a_fresh_cache_free_compile(string fixture)
    {
        using Baked baked = Bake(fixture);

        // An empty bake would pass every loop below.
        baked.Fresh.ChunkMeshes.Count.ShouldBeGreaterThan(0, "the fixture compiled to no geometry");
        baked.Scene.StaticWorldChunkMeshes.Count.ShouldBe(baked.Fresh.ChunkMeshes.Count);

        baked.Report.TriangleCount.ShouldBe(Triangles(baked.Fresh));

        foreach (ChunkMesh expected in baked.Fresh.ChunkMeshes)
        {
            baked.Scene
                .TryGetStaticWorldChunkMesh(expected.Coord, out StaticWorldChunkMesh loaded)
                .ShouldBeTrue($"cell {expected.Coord} is missing from the loaded world");

            ShouldBeBits(loaded.RenderBounds.Min, expected.RenderBounds.Min, $"{expected.Coord} bounds min");
            ShouldBeBits(loaded.RenderBounds.Max, expected.RenderBounds.Max, $"{expected.Coord} bounds max");

            loaded.Submeshes.Length.ShouldBe(
                expected.Submeshes.Count, $"cell {expected.Coord} submesh count");

            loaded.Submeshes.Select(s => s.SourceMaterial).Distinct().Count()
                .ShouldBe(loaded.Submeshes.Length, $"cell {expected.Coord} names a material twice");

            // Matched by material, not position: the file sorts by asset row,
            // the compile by material id.
            foreach (ChunkSubmesh submesh in expected.Submeshes)
            {
                StaticWorldSubmesh actual = loaded.Submeshes
                    .Single(s => s.SourceMaterial == submesh.Material);

                var mesh = (FakeMesh)actual.Mesh;
                ShouldBeBits(
                    mesh.VertexData, submesh.Vertices,
                    $"{expected.Coord} {Describe(submesh.Material)} vertices");

                mesh.IndexData.ShouldBe(
                    submesh.Indices, $"{expected.Coord} {Describe(submesh.Material)} indices");
            }
        }
    }

    [Theory]
    [InlineData(BakeCorpus.FlushCoplanarRoom)]
    [InlineData(BakeCorpus.Cavities)]
    [InlineData(BakeCorpus.Palette)]
    [InlineData(BakeCorpus.Sprawl)]
    public void The_loaded_bsp_nodes_are_bit_identical_to_a_fresh_flatten(string fixture)
    {
        using Baked baked = Bake(fixture);

        CompiledStaticWorld adopted = baked.Scene.CompiledStaticWorld.ShouldNotBeNull();
        IReadOnlyList<WorldChunk> cells = baked.Fresh.Chunks.OrderedChunks;

        // File order is OrderedChunks order, so compare by position.
        adopted.Chunks.Count.ShouldBe(cells.Count, "cell count");
        adopted.Chunks.Count.ShouldBeGreaterThan(0);

        for (int i = 0; i < cells.Count; i++)
        {
            WorldChunk cell = cells[i];
            CompiledStaticWorldChunk loaded = adopted.Chunks[i];

            loaded.Coord.ShouldBe(cell.Coord, $"cell {i} is out of order");

            // A cell with no mesh gets the cell cube as its bounds.
            if (loaded.TriangleCount == 0)
            {
                ShouldBeBits(loaded.RenderBounds.Min, cell.Coord.Bounds.Min, $"cell {cell.Coord} empty min");
                ShouldBeBits(loaded.RenderBounds.Max, cell.Coord.Bounds.Max, $"cell {cell.Coord} empty max");
            }

            if (cell.Bsp is null)
            {
                loaded.Bsp.ShouldBeNull($"cell {cell.Coord} grew a tree the compile did not build");
                continue;
            }

            FlatBspNode[] expected = BspFlattener.Flatten(cell.Bsp, out int rootIndex);
            FlatBspTree tree = loaded.Bsp.ShouldNotBeNull(
                $"cell {cell.Coord} lost the tree the compile built for it");

            tree.RootIndex.ShouldBe(rootIndex, $"cell {cell.Coord} root");
            tree.NodeCount.ShouldBe(expected.Length, $"cell {cell.Coord} node count");

            ReadOnlySpan<FlatBspNode> actual = tree.Nodes.Span;
            for (int n = 0; n < expected.Length; n++)
            {
                string what = $"cell {cell.Coord} node {n}";

                ShouldBeBits(actual[n].Plane.Normal, expected[n].Plane.Normal, $"{what} normal");
                ShouldBeBits(actual[n].Plane.D, expected[n].Plane.D, $"{what} d");

                actual[n].Front.ShouldBe(expected[n].Front, $"{what} front");
                actual[n].Back.ShouldBe(expected[n].Back, $"{what} back");
            }
        }
    }

    [Theory]
    [InlineData(BakeCorpus.FlushCoplanarRoom)]
    [InlineData(BakeCorpus.Cavities)]
    [InlineData(BakeCorpus.Palette)]
    [InlineData(BakeCorpus.Sprawl)]
    public void The_adopted_trees_answer_a_LATTICE_of_points_exactly_as_a_fresh_compile_does(string fixture)
    {
        // Identical node arrays can still be routed to the wrong cell. Only a
        // query through ChunkCoord.FromPosition sees that.
        using Baked baked = Bake(fixture);

        CompiledStaticWorld adopted = baked.Scene.CompiledStaticWorld.ShouldNotBeNull();
        Aabb bounds = WorldBounds(baked.Fresh);

        int solid = 0;
        int empty = 0;
        const int Steps = 13;

        for (int x = 0; x < Steps; x++)
        {
            for (int y = 0; y < Steps; y++)
            {
                for (int z = 0; z < Steps; z++)
                {
                    var point = new Vector3(
                        Lerp(bounds.Min.X, bounds.Max.X, x, Steps),
                        Lerp(bounds.Min.Y, bounds.Max.Y, y, Steps),
                        Lerp(bounds.Min.Z, bounds.Max.Z, z, Steps));

                    bool expected = baked.Fresh.ContainsPoint(point);
                    adopted.ContainsPoint(point).ShouldBe(expected, $"the baked world disagrees at {point}");

                    if (expected) solid++;
                    else empty++;
                }
            }
        }

        solid.ShouldBeGreaterThan(0, "the lattice never landed inside the level");
        empty.ShouldBeGreaterThan(0, "the lattice never landed outside the level");
    }

    [Theory]
    [InlineData(BakeCorpus.FlushCoplanarRoom)]
    [InlineData(BakeCorpus.Cavities)]
    [InlineData(BakeCorpus.Palette)]
    [InlineData(BakeCorpus.Sprawl)]
    public void The_submesh_directory_is_in_ascending_asset_order_in_every_cell(string fixture)
    {
        using Baked baked = Bake(fixture);

        ScmapProbe map = ScmapProbe.Read(baked.File);
        var seen = new List<uint>();

        foreach (ScmapProbe.CellGeometry cell in map.Geometry)
        {
            for (int s = 1; s < cell.Submeshes.Count; s++)
            {
                cell.Submeshes[s].AssetIndex.ShouldBeGreaterThan(
                    cell.Submeshes[s - 1].AssetIndex,
                    $"cell ({cell.X},{cell.Y},{cell.Z}) directory is not strictly ascending");
            }

            foreach (ScmapProbe.SubmeshCopy submesh in cell.Submeshes) seen.Add(submesh.AssetIndex);
        }

        seen.Count.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(BakeCorpus.FlushCoplanarRoom)]
    [InlineData(BakeCorpus.Cavities)]
    [InlineData(BakeCorpus.Palette)]
    [InlineData(BakeCorpus.Sprawl)]
    public void Loading_any_fixture_in_the_corpus_runs_no_carve_at_all(string fixture)
    {
        using Baked baked = Bake(fixture);

        baked.CarvesDuringLoad.ShouldBe(0, "a compiled map must reach the GPU with no CSG at all");
        baked.Scene.StaticWorld.ShouldBeNull();
        baked.Scene.StaticWorldCompileCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(BakeCorpus.FlushCoplanarRoom)]
    [InlineData(BakeCorpus.Cavities)]
    [InlineData(BakeCorpus.Palette)]
    [InlineData(BakeCorpus.Sprawl)]
    public void Every_fixture_exercises_what_it_claims(string fixture)
    {
        // Measured off the compile, so a fixture that drifts fails here.
        using Baked baked = Bake(fixture);

        CompiledStaticWorld adopted = baked.Scene.CompiledStaticWorld.ShouldNotBeNull();
        int widest = baked.Fresh.ChunkMeshes.Max(m => m.Submeshes.Count);
        int treesWithoutMeshes = adopted.Chunks.Count(c => c.Bsp is not null && c.TriangleCount == 0);

        switch (fixture)
        {
            case BakeCorpus.FlushCoplanarRoom:
                widest.ShouldBeGreaterThanOrEqualTo(2);
                HasSubtractiveBrush(baked.Source).ShouldBeTrue();
                break;

            case BakeCorpus.Cavities:
                HasSubtractiveBrush(baked.Source).ShouldBeTrue();

                CsgWorld uncut = Compile(baked.Corpus.BuildSceneWithoutCuts());
                Triangles(baked.Fresh).ShouldNotBe(
                    Triangles(uncut), "the cuts changed no geometry at all");

                // Pocket and tunnel centres: empty when carved, solid when not.
                foreach (Vector3 inside in new[] { new Vector3(5f, 0f, 5f), Vector3.Zero })
                {
                    baked.Fresh.ContainsPoint(inside).ShouldBeFalse($"nothing was removed at {inside}");
                    uncut.ContainsPoint(inside).ShouldBeTrue($"{inside} was never inside the slab");
                }

                break;

            case BakeCorpus.Palette:
                // Six painted faces and the bare brush.
                widest.ShouldBeGreaterThanOrEqualTo(7);

                AssetOrderDiffersFromMaterialIdOrder(baked).ShouldBeTrue(
                    "the file's asset order is the compile's material order, so the sort is untested here");
                break;

            case BakeCorpus.Sprawl:
                adopted.Chunks.Count.ShouldBeGreaterThanOrEqualTo(12, "cells");

                treesWithoutMeshes.ShouldBeGreaterThan(0);

                // One material everywhere: the control for submesh ordering.
                widest.ShouldBe(1);

                HasObliquePlane(adopted).ShouldBeTrue("nothing in this level is off-axis");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unclaimed fixture.");
        }
    }

    [Fact]
    public void A_level_bakes_the_same_chunks_with_and_without_its_entities()
    {
        // A door and its trigger are parts, and a part never enters the carve.
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteBundle(project, "Plain.smap");
        fixture.WriteBundle(project, "Doors.smap", withEntities: true);

        byte[] plain = BakeFile(project, "Maps/Plain.smap");
        byte[] doors = BakeFile(project, "Maps/Doors.smap");

        ScmapProbe.Read(plain).Entities.ShouldBeEmpty();
        ScmapProbe.Read(doors).Entities.Count.ShouldBe(4);

        // The asset table too: a submesh names its material by row.
        foreach (uint section in new[]
        {
            ScmapFormat.AssetSection,
            ScmapFormat.ChunkDirectorySection,
            ScmapFormat.ChunkMeshSection,
            ScmapFormat.ChunkBspSection,
        })
        {
            byte[] expected = ScmapSurgery.Body(plain, section);
            expected.Length.ShouldBeGreaterThan(0);

            ScmapSurgery.Body(doors, section).ShouldBe(expected, ScmapFormat.DescribeFourCc(section));
        }
    }

    [Fact]
    public void The_corpus_is_enumerated_in_one_place()
    {
        // InlineData cannot read BakeCorpus.Names, so the rows above repeat it.
        BakeCorpus.Names.ShouldBe(
            [BakeCorpus.FlushCoplanarRoom, BakeCorpus.Cavities, BakeCorpus.Palette, BakeCorpus.Sprawl]);
    }

    // One fixture, cooked, loaded, and compiled again beside itself.
    private sealed class Baked : IDisposable
    {
        public required BakeCorpus Corpus { get; init; }
        public required TempProject Project { get; init; }
        public required byte[] File { get; init; }

        // The scene the compiled map was loaded into.
        public required SpectraEngine.Core.Scene.Scene Scene { get; init; }

        public required FakeRenderer Renderer { get; init; }

        // The authored scene, re-bound from the bundle on disk.
        public required SpectraEngine.Core.Scene.Scene Source { get; init; }

        // Cache-free compile of Source.
        public required CsgWorld Fresh { get; init; }

        public required CompiledMapLoadReport Report { get; init; }

        public required long CarvesDuringLoad { get; init; }

        public void Dispose()
        {
            Scene.ReleaseCompiledStaticWorld(Renderer);
            Project.Dispose();
        }
    }

    private static Baked Bake(string fixture)
    {
        BakeCorpus corpus = BakeCorpus.Fresh(fixture);
        var project = new TempProject();

        try
        {
            MapFixture.WriteBundle(project, "Level.smap", corpus.BuildScene());

            var context = new RuleContext(project.Root, "Maps/Level.smap", CookProfile.Ship);
            new MapRule().Cook(context);

            context.Diagnostics.Count.ShouldBe(
                0, string.Join(Environment.NewLine, context.Diagnostics.Select(d => d.ToString())));

            byte[] file = context.Emissions[0].Payload;

            var renderer = new FakeRenderer();
            var scene = new SpectraEngine.Core.Scene.Scene("empty");

            // Thread-static counter, so parallel suites cannot move it.
            long before = Csg.CarveInvocationsOnThisThread;
            CompiledMapLoadReport report =
                CompiledMapLoader.Load(scene, renderer, ContentBlob.CopyOf(file), "Maps/Level.scmap");
            long carves = Csg.CarveInvocationsOnThisThread - before;

            // From the bundle on disk, so the text round trip is covered too.
            MapDocument document = MapBundle.Load(Path.Combine(project.Layout.MapsPath, "Level.smap"));
            var source = new SpectraEngine.Core.Scene.Scene(document.Scene.Name);
            MapSceneBinder.ApplyTo(document, source);

            return new Baked
            {
                Corpus = corpus,
                Project = project,
                File = file,
                Scene = scene,
                Renderer = renderer,
                Source = source,
                Fresh = Compile(source),
                Report = report,
                CarvesDuringLoad = carves,
            };
        }
        catch
        {
            project.Dispose();
            throw;
        }
    }

    private static byte[] BakeFile(TempProject project, string bundlePath)
    {
        var context = new RuleContext(project.Root, bundlePath, CookProfile.Ship);
        new MapRule().Cook(context);

        context.Diagnostics.Count.ShouldBe(
            0, string.Join(Environment.NewLine, context.Diagnostics.Select(d => d.ToString())));

        return context.Emissions[0].Payload;
    }

    private static CsgWorld Compile(SpectraEngine.Core.Scene.Scene scene)
    {
        IReadOnlyList<BrushPlacement> placements =
            scene.CaptureStaticWorldPlacements(out string? defect).ShouldNotBeNull(defect);

        // Cache-free, as the bake is.
        return CsgWorld.Build(placements);
    }

    private static bool HasObliquePlane(CompiledStaticWorld world)
    {
        foreach (CompiledStaticWorldChunk chunk in world.Chunks)
        {
            if (chunk.Bsp is not { } tree) continue;

            ReadOnlySpan<FlatBspNode> nodes = tree.Nodes.Span;
            for (int i = 0; i < nodes.Length; i++)
            {
                Vector3 n = nodes[i].Plane.Normal;
                if (n.X != 0f && n.Y != 0f && n.Z != 0f) return true;
            }
        }

        return false;
    }

    private static float Lerp(float min, float max, int step, int steps) =>
        min + ((max - min) * step / (steps - 1));

    // Inflated so the lattice also lands outside the level.
    private static Aabb WorldBounds(CsgWorld world)
    {
        Aabb bounds = world.ChunkMeshes[0].RenderBounds;
        foreach (ChunkMesh mesh in world.ChunkMeshes)
        {
            bounds = new Aabb(
                Vector3.Min(bounds.Min, mesh.RenderBounds.Min),
                Vector3.Max(bounds.Max, mesh.RenderBounds.Max));
        }

        var margin = new Vector3(1.5f);
        return new Aabb(bounds.Min - margin, bounds.Max + margin);
    }

    private static int Triangles(CsgWorld world)
    {
        int indices = 0;
        foreach (ChunkMesh mesh in world.ChunkMeshes)
        {
            foreach (ChunkSubmesh submesh in mesh.Submeshes) indices += submesh.Indices.Length;
        }

        return indices / 3;
    }

    private static bool HasSubtractiveBrush(SpectraEngine.Core.Scene.Scene scene) =>
        scene.Root.Traverse().Any(n => n.Brush is { Operation: BrushOperation.Subtractive });

    private static bool AssetOrderDiffersFromMaterialIdOrder(Baked baked)
    {
        ScmapProbe map = ScmapProbe.Read(baked.File);

        foreach (ChunkMesh mesh in baked.Fresh.ChunkMeshes)
        {
            ScmapProbe.CellGeometry? cell = map.Geometry.SingleOrDefault(
                c => c.X == mesh.Coord.X && c.Y == mesh.Coord.Y && c.Z == mesh.Coord.Z);

            if (cell is null || cell.Submeshes.Count < 2) continue;

            var asCompiled = mesh.Submeshes.Select(s => RowOf(map, s.Material)).ToArray();
            var asWritten = cell.Submeshes.Select(s => s.AssetIndex).ToArray();

            if (!asCompiled.SequenceEqual(asWritten)) return true;
        }

        return false;
    }

    private static uint RowOf(ScmapProbe map, MaterialRef material)
    {
        if (material.IsDefault) return ScmapFormat.NoAssetIndex;

        MaterialRegistry.TryGetPath(material, out string path).ShouldBeTrue();
        int row = map.Assets.FindIndex(
            a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));

        row.ShouldBeGreaterThanOrEqualTo(0, $"'{path}' has no asset row");
        return (uint)row;
    }

    private static string Describe(MaterialRef material) =>
        material.IsDefault || !MaterialRegistry.TryGetPath(material, out string path)
            ? "(default material)"
            : path;

    private static void ShouldBeBits(float[] actual, float[] expected, string what)
    {
        actual.Length.ShouldBe(expected.Length, $"{what}: length");
        for (int i = 0; i < expected.Length; i++) ShouldBeBits(actual[i], expected[i], $"{what}[{i}]");
    }

    private static void ShouldBeBits(Vector3 actual, Vector3 expected, string what)
    {
        ShouldBeBits(actual.X, expected.X, $"{what}.x");
        ShouldBeBits(actual.Y, expected.Y, $"{what}.y");
        ShouldBeBits(actual.Z, expected.Z, $"{what}.z");
    }

    // Bits, not ==: 0f equals -0f and NaN never equals itself.
    private static void ShouldBeBits(float actual, float expected, string what)
    {
        int actualBits = BitConverter.SingleToInt32Bits(actual);
        int expectedBits = BitConverter.SingleToInt32Bits(expected);

        if (actualBits == expectedBits) return;

        actualBits.ShouldBe(
            expectedBits,
            $"{what}: got {actual:R} (0x{actualBits:X8}), expected {expected:R} (0x{expectedBits:X8})");
    }
}
