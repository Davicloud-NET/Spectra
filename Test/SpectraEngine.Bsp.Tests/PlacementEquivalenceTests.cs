using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Carving a brush at a <see cref="BrushPlacement"/> snapshot transform must
/// match carving it at its own <see cref="Brush.Transform"/>, bit for bit.
/// </summary>
public sealed class PlacementEquivalenceTests
{
    [Fact]
    public void Carve_over_placements_matches_carve_over_brush_transforms()
    {
        (Brush[] brushes, BrushPlacement[] placements) = CreateEquivalentSets();

        Polygon[] viaTransforms = Csg.Carve(brushes);
        Polygon[] viaPlacements = Csg.Carve(placements);

        viaTransforms.ShouldNotBeEmpty();
        ShouldBeIdenticalSurfaces(viaTransforms, viaPlacements);
    }

    [Fact]
    public void World_built_from_placements_matches_world_built_from_brush_transforms()
    {
        (Brush[] brushes, BrushPlacement[] placements) = CreateEquivalentSets();

        CsgWorld viaTransforms = CsgWorld.Build(brushes);
        CsgWorld viaPlacements = CsgWorld.Build(placements);

        viaTransforms.Surfaces.ShouldNotBeEmpty();
        ShouldBeIdenticalSurfaces(viaTransforms.Surfaces, viaPlacements.Surfaces);

        (float[] expectedVertices, uint[] expectedIndices) = viaTransforms.BuildMesh();
        (float[] actualVertices, uint[] actualIndices) = viaPlacements.BuildMesh();
        actualVertices.SequenceEqual(expectedVertices).ShouldBeTrue();
        actualIndices.SequenceEqual(expectedIndices).ShouldBeTrue();
    }

    [Fact]
    public void Building_the_same_placements_twice_is_deterministic()
    {
        // Carve and weld run in parallel; scheduling must not reorder output.
        (_, BrushPlacement[] placements) = CreateEquivalentSets();

        CsgWorld first = CsgWorld.Build(placements);
        CsgWorld second = CsgWorld.Build(placements);

        ShouldBeIdenticalSurfaces(first.Surfaces, second.Surfaces);

        (float[] firstVertices, uint[] firstIndices) = first.BuildMesh();
        (float[] secondVertices, uint[] secondIndices) = second.BuildMesh();
        secondVertices.SequenceEqual(firstVertices).ShouldBeTrue();
        secondIndices.SequenceEqual(firstIndices).ShouldBeTrue();
    }

    // The placement set uses fresh brushes with a default Transform: a placement
    // overload that read Brush.Transform would land everything at the origin.
    private static (Brush[] Brushes, BrushPlacement[] Placements) CreateEquivalentSets()
    {
        Matrix4x4[] transforms =
        [
            Matrix4x4.CreateTranslation(0.5f, 0f, 0f),
            Matrix4x4.CreateRotationY(MathF.PI / 4f) * Matrix4x4.CreateTranslation(1.5f, 0.25f, 0.5f),
        ];

        var brushes = new Brush[transforms.Length];
        var placements = new BrushPlacement[transforms.Length];
        for (int i = 0; i < transforms.Length; i++)
        {
            brushes[i] = CreateCentredUnitBox();
            brushes[i].Transform = transforms[i];
            placements[i] = new BrushPlacement(CreateCentredUnitBox(), transforms[i]);
        }

        return (brushes, placements);
    }

    private static Brush CreateCentredUnitBox() =>
        Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));

    // Bit-exact: both paths run the same code over the same floats.
    private static void ShouldBeIdenticalSurfaces(IReadOnlyList<Polygon> expected, IReadOnlyList<Polygon> actual)
    {
        actual.Count.ShouldBe(expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            actual[i].Surface.ShouldBe(expected[i].Surface);
            actual[i].VertexCount.ShouldBe(expected[i].VertexCount);
            for (int v = 0; v < expected[i].VertexCount; v++)
                actual[i].Vertices[v].ShouldBe(expected[i].Vertices[v]);
        }
    }
}
