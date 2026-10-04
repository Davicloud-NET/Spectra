using SpectraEngine.Core.Graphics.D3D12;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The bucket function behind D3D12's mesh buffer pool.
/// </summary>
public sealed class MeshBufferPoolTests
{
    [Theory]
    [InlineData(0u, 256u)]
    [InlineData(1u, 256u)]
    [InlineData(256u, 256u)]
    [InlineData(257u, 512u)]
    [InlineData(1000u, 1024u)]
    [InlineData(1024u, 1024u)]
    [InlineData(1025u, 2048u)]
    [InlineData(100_000u, 131_072u)]
    [InlineData(0x80000000u, 0x80000000u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void A_request_rounds_up_to_its_bucket(uint requested, uint expected)
    {
        // 256 is D3D12's buffer alignment.
        D3D12Renderer.MeshBufferBucket(requested).ShouldBe(expected);
    }

    [Fact]
    public void Sizes_that_differ_by_a_few_triangles_share_a_bucket()
    {
        // A recompiled chunk changes size by a few triangles; an exact-size
        // pool would never hit.
        const uint baseline = 40_000;
        uint bucket = D3D12Renderer.MeshBufferBucket(baseline);

        for (uint delta = 0; delta < 4_000; delta += 137)
            D3D12Renderer.MeshBufferBucket(baseline + delta).ShouldBe(bucket);
    }
}
