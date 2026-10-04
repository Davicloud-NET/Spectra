using System;
using System.IO;
using SpectraEngine.Core.Graphics.D3D11;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The context-level SRV/sampler skip cache behind D3D11's SetTexture.
/// </summary>
// A skipped rebind after the context's slots were cleared samples null SRVs,
// which D3D11 reads as zeros with no error anywhere.
public sealed class D3D11BindCacheTests
{
    [Fact]
    public void The_first_bind_of_a_slot_is_issued()
    {
        var cache = new D3D11BindCache();
        cache.MustBind(0, srv: 0x10, sampler: 0x20).ShouldBeTrue();
    }

    [Fact]
    public void Repeating_the_same_pair_is_skipped()
    {
        var cache = new D3D11BindCache();
        cache.MustBind(0, 0x10, 0x20);
        cache.MustBind(0, 0x10, 0x20).ShouldBeFalse();
    }

    [Fact]
    public void Changing_either_half_of_the_pair_rebinds()
    {
        var cache = new D3D11BindCache();
        cache.MustBind(0, 0x10, 0x20);
        cache.MustBind(0, 0x11, 0x20).ShouldBeTrue("a different SRV must be bound");
        cache.MustBind(0, 0x11, 0x21).ShouldBeTrue("a different sampler must be bound");
    }

    [Fact]
    public void Slots_are_independent()
    {
        var cache = new D3D11BindCache();
        cache.MustBind(0, 0x10, 0x20);
        cache.MustBind(5, 0x10, 0x20).ShouldBeTrue("slot 5 has never seen this pair");
    }

    [Fact]
    public void Reset_forces_every_slot_to_rebind()
    {
        var cache = new D3D11BindCache();
        cache.MustBind(0, 0x10, 0x20);
        cache.MustBind(3, 0x30, 0x40);

        cache.Reset();

        cache.MustBind(0, 0x10, 0x20).ShouldBeTrue();
        cache.MustBind(3, 0x30, 0x40).ShouldBeTrue();
    }

    [Fact]
    public void A_slot_outside_the_tracked_range_always_binds()
    {
        // The unbind only clears TrackedSlots registers; past them nothing resets.
        var cache = new D3D11BindCache();
        uint outside = D3D11BindCache.TrackedSlots;
        cache.MustBind(outside, 0x10, 0x20).ShouldBeTrue();
        cache.MustBind(outside, 0x10, 0x20).ShouldBeTrue("no skip without a reset contract");
    }

    [Fact]
    public void Every_context_clearing_site_in_the_renderer_resets_the_cache()
    {
        // The wiring needs a device to run, so it is checked in the source.
        string source = File.ReadAllText(RendererSourcePath());

        int unbind = source.IndexOf("PSSetShaderResources(0, Slots, none);", StringComparison.Ordinal);
        unbind.ShouldBeGreaterThanOrEqualTo(0, "the SRV unbind site moved; update this test with it");
        NearbyReset(source, unbind).ShouldBeTrue(
            "UnbindPixelShaderResources cleared the context's slots without resetting the bind cache; " +
            "the next pass will skip a needed rebind and silently sample null");

        int clearState = source.IndexOf("ClearState();", StringComparison.Ordinal);
        clearState.ShouldBeGreaterThanOrEqualTo(0, "the resize path's ClearState moved; update this test with it");
        NearbyReset(source, clearState).ShouldBeTrue(
            "the resize path's ClearState wiped the context without resetting the bind cache");
    }

    private static bool NearbyReset(string source, int fromIndex)
    {
        int window = Math.Min(source.Length - fromIndex, 500);
        return source.AsSpan(fromIndex, window).IndexOf("_bindCache.Reset();".AsSpan(), StringComparison.Ordinal) >= 0;
    }

    private static string RendererSourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "SpectraEngine.Core", "Graphics", "D3D11", "D3D11Renderer.cs");
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"No solution file above {AppContext.BaseDirectory}; this source-convention test needs the repo.");
    }
}
