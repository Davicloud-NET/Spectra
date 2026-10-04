using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="VertexSnapper"/>: sub-grid noise collapses to identical floats,
/// snapping is idempotent, inputs are not mutated.
/// </summary>
public sealed class VertexSnapperTests
{
    private static readonly Plane AnyPlane = new(Vector3.UnitY, 0f);

    private static Vector3 SnapSingle(Vector3 v) =>
        VertexSnapper.Snap([new Polygon([v, v + Vector3.UnitX, v + Vector3.UnitZ], AnyPlane)])[0].Vertices[0];

    [Fact]
    public void Subgrid_noise_collapses_to_bit_identical_floats()
    {
        // Either side of 0.3, much closer together than the 1e-4 grid.
        Vector3 a = SnapSingle(new Vector3(0.2999999f, -0.2999999f, 7.0000002f));
        Vector3 b = SnapSingle(new Vector3(0.3000001f, -0.3000001f, 6.9999998f));

        a.ShouldBe(b);
    }

    [Fact]
    public void Snapped_values_lie_on_the_grid()
    {
        Vector3 snapped = SnapSingle(new Vector3(0.12345678f, -3.9876543f, 42.000037f));

        // Loose tolerance: grid indices reach ~4e5, where a ULP is ~0.03.
        foreach (float c in new[] { snapped.X, snapped.Y, snapped.Z })
        {
            float index = c / VertexSnapper.GridSize;
            MathF.Abs(index - MathF.Round(index)).ShouldBeLessThan(0.1f);
        }
    }

    [Theory]
    [InlineData(0.7f, -0.12345f, 42.4242f)]
    [InlineData(0.2999999f, 100.00007f, -0.0001f)]
    [InlineData(0f, 1f, -256f)]
    public void Snapping_is_idempotent(float x, float y, float z)
    {
        Vector3 once = SnapSingle(new Vector3(x, y, z));
        Vector3 twice = SnapSingle(once);

        twice.ShouldBe(once);
    }

    [Fact]
    public void Snapping_moves_a_vertex_by_at_most_half_a_grid_step()
    {
        // Values kept away from half-grid boundaries.
        var v = new Vector3(0.12348f, -7.65432f, 99.99992f);
        Vector3 snapped = SnapSingle(v);

        MathF.Abs(snapped.X - v.X).ShouldBeLessThan(VertexSnapper.GridSize * 0.501f);
        MathF.Abs(snapped.Y - v.Y).ShouldBeLessThan(VertexSnapper.GridSize * 0.501f);
        MathF.Abs(snapped.Z - v.Z).ShouldBeLessThan(VertexSnapper.GridSize * 0.501f);
    }

    [Fact]
    public void Snap_returns_new_polygons_and_leaves_inputs_untouched()
    {
        var original = new Vector3(0.12345678f, 0f, 0f);
        var poly = new Polygon([original, original + Vector3.UnitX, original + Vector3.UnitZ], AnyPlane);

        Polygon[] snapped = VertexSnapper.Snap([poly]);

        snapped[0].ShouldNotBeSameAs(poly);
        poly.Vertices[0].ShouldBe(original);          // input polygon unmodified
        snapped[0].Surface.ShouldBe(poly.Surface);    // only vertices change
    }
}
