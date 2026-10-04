using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A real level (the demo play area) saved and loaded compiles to the same
/// world, vertex for vertex.
/// </summary>
public sealed class MapLevelFidelityTests
{
    private static Scene BuildPlayArea()
    {
        var scene = new Scene("PlayArea");
        DemoPlayArea.Build(scene, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default);
        return scene;
    }

    [Fact]
    public void The_demo_play_area_compiles_identically_after_a_save_and_a_load()
    {
        Scene authored = BuildPlayArea();
        authored.RebuildStaticWorld(new FakeRenderer());

        Scene reloaded = new("Empty");
        MapSceneBinder.ApplyTo(
            MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(authored))), reloaded);
        reloaded.RebuildStaticWorld(new FakeRenderer());

        CsgWorld before = authored.StaticWorld.ShouldNotBeNull();
        CsgWorld after = reloaded.StaticWorld.ShouldNotBeNull();

        // Two empty worlds would compare equal.
        before.ChunkMeshes.Count.ShouldBeGreaterThan(4,
            "the play area must actually compile to something for this comparison to mean anything");

        after.ChunkMeshes.Count.ShouldBe(before.ChunkMeshes.Count, "chunk count");

        for (int c = 0; c < before.ChunkMeshes.Count; c++)
        {
            ChunkMesh a = before.ChunkMeshes[c];
            ChunkMesh b = after.ChunkMeshes[c];

            b.Coord.ShouldBe(a.Coord, $"chunk {c} coordinate");
            b.Submeshes.Count.ShouldBe(a.Submeshes.Count, $"chunk {a.Coord} submesh count");

            for (int s = 0; s < a.Submeshes.Count; s++)
            {
                ChunkSubmesh want = a.Submeshes[s];
                ChunkSubmesh got = b.Submeshes[s];

                got.Material.ShouldBe(want.Material, $"chunk {a.Coord} submesh {s} material");
                got.Vertices.ShouldBe(want.Vertices, $"chunk {a.Coord} submesh {s} vertices");
                got.Indices.ShouldBe(want.Indices, $"chunk {a.Coord} submesh {s} indices");
            }
        }
    }

    [Fact]
    public void The_demo_play_area_survives_a_second_round_trip_byte_for_byte()
    {
        // The first save may change bytes: Brush re-normalises its planes.
        // After that nothing may move.
        Scene authored = BuildPlayArea();

        byte[] first = MapWriter.Write(MapSceneBinder.FromScene(authored));

        Scene reloaded = new("Empty");
        MapSceneBinder.ApplyTo(MapReader.Read(first), reloaded);
        byte[] second = MapWriter.Write(MapSceneBinder.FromScene(reloaded));

        if (!first.AsSpan().SequenceEqual(second))
        {
            string want = Encoding.UTF8.GetString(first);
            string got = Encoding.UTF8.GetString(second);
            int at = 0;
            while (at < want.Length && at < got.Length && want[at] == got[at]) at++;
            throw new Xunit.Sdk.XunitException(
                $"The level changed on the second save, first at character {at}:\n"
                + $"  expected: {Excerpt(want, at)}\n  actual:   {Excerpt(got, at)}");
        }
    }

    [Fact]
    public void The_part_brush_in_the_course_is_still_a_part_after_a_load()
    {
        // The chunk comparison would catch a lost BrushKind; this says which bit broke.
        Scene authored = BuildPlayArea();

        Scene reloaded = new("Empty");
        MapSceneBinder.ApplyTo(
            MapReader.Read(MapWriter.Write(MapSceneBinder.FromScene(authored))), reloaded);

        CountKinds(authored.Root, out int authoredParts, out int authoredSubtractive);
        CountKinds(reloaded.Root, out int reloadedParts, out int reloadedSubtractive);

        authoredParts.ShouldBeGreaterThan(0, "the course is supposed to contain a part brush");
        authoredSubtractive.ShouldBeGreaterThan(0, "the course is supposed to contain subtractive brushes");
        reloadedParts.ShouldBe(authoredParts);
        reloadedSubtractive.ShouldBe(authoredSubtractive);
    }

    private static void CountKinds(SceneNode node, out int parts, out int subtractive)
    {
        int p = 0, s = 0;
        var stack = new Stack<SceneNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            SceneNode current = stack.Pop();
            if (current.Brush is { } brush)
            {
                if (current.BrushKind == BrushKind.Part) p++;
                if (brush.Operation == BrushOperation.Subtractive) s++;
            }
            foreach (SceneNode child in current.Children) stack.Push(child);
        }
        parts = p;
        subtractive = s;
    }

    private static string Excerpt(string text, int at) =>
        text.Substring(System.Math.Max(0, at - 40), System.Math.Min(80, text.Length - System.Math.Max(0, at - 40)))
            .Replace("\n", "\\n");
}
