using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// CSG runs in brush-local frames, so a rigid motion of the whole brush set
/// must not change the carve result.
/// </summary>
public sealed class OriginInvarianceTests
{
    // No coincident or near-coplanar planes, every vertex >= 0.2 from every
    // carver plane: a rigid motion cannot flip a classification.
    private static (Brush A, Brush B) CreateOverlappingPair(Matrix4x4 rigidMotion)
    {
        Brush a = Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        Brush b = Brush.CreateBox(new Vector3(0.3f, -0.4f, -0.6f), new Vector3(2.3f, 1.2f, 0.7f));
        a.Transform *= rigidMotion;
        b.Transform *= rigidMotion;
        return (a, b);
    }

    private static void AssertCarveMatchesOriginBaseline(Matrix4x4 rigidMotion, float tolerance)
    {
        var (a0, b0) = CreateOverlappingPair(Matrix4x4.Identity);
        Polygon[] baseline = Csg.Carve([a0, b0]);
        baseline.ShouldNotBeEmpty();

        var (a1, b1) = CreateOverlappingPair(rigidMotion);
        Polygon[] moved = Csg.Carve([a1, b1]);

        Matrix4x4.Invert(rigidMotion, out Matrix4x4 inverse).ShouldBeTrue();

        // Carve output order is deterministic, so compare positionally.
        moved.Length.ShouldBe(baseline.Length);
        for (int p = 0; p < baseline.Length; p++)
        {
            moved[p].VertexCount.ShouldBe(baseline[p].VertexCount, $"vertex count of polygon {p}");
            for (int v = 0; v < baseline[p].VertexCount; v++)
            {
                Vector3 mappedBack = Vector3.Transform(moved[p].Vertices[v], inverse);
                Vector3.Distance(mappedBack, baseline[p].Vertices[v])
                    .ShouldBeLessThan(tolerance, $"vertex {v} of polygon {p}");
            }
        }
    }

    [Fact]
    public void Carve_is_invariant_under_far_translation() =>
        AssertCarveMatchesOriginBaseline(Matrix4x4.CreateTranslation(8192f, 0f, 0f), 1e-3f);

    [Fact]
    public void Carve_is_invariant_under_rotation() =>
        AssertCarveMatchesOriginBaseline(Matrix4x4.CreateRotationY(MathF.PI / 6f), 1e-3f);

    [Fact]
    public void Carve_is_invariant_under_combined_rotation_and_far_translation()
    {
        // A float ulp at 8192 is ~1e-3, so allow a few.
        Matrix4x4 rigid = Matrix4x4.CreateRotationY(MathF.PI / 6f) * Matrix4x4.CreateTranslation(8192f, 0f, 4096f);
        AssertCarveMatchesOriginBaseline(rigid, 5e-3f);
    }
}
