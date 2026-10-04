using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;

namespace SpectraEngine.Core.Graphics.D3D11;

// A texture usable as both render target and shader resource, with optional depth.
// The renderer nulls the pixel-shader SRV slots at BeginPass so the colour
// attachment is never bound as input and output at once.
internal sealed unsafe class D3D11RenderTarget : RenderTarget
{
    private readonly ComPtr<ID3D11Device> _device;
    private readonly D3D11Texture? _color;
    private readonly D3D11Texture? _depth;
    private ComPtr<ID3D11RenderTargetView> _rtv;
    private ComPtr<ID3D11DepthStencilView> _dsv;
    private bool _disposed;

    internal ID3D11RenderTargetView* Rtv => (ID3D11RenderTargetView*)_rtv.Handle;
    internal ID3D11DepthStencilView* Dsv => (ID3D11DepthStencilView*)_dsv.Handle;

    internal D3D11RenderTarget(ComPtr<ID3D11Device> device, in RenderTargetDesc desc)
    {
        desc.Validate();

        _device = device;
        Desc = desc;

        if (desc.Color)
        {
            _color = D3D11Texture.CreateRenderTargetTexture(
                device, desc.Width, desc.Height, desc.ColorFormat, desc.ColorSpace, desc.Filter, desc.Wrap,
                desc.Sharing);
        }

        if (desc.Depth)
            _depth = D3D11Texture.CreateDepthTexture(device, desc.Width, desc.Height);

        Allocate(desc.Width, desc.Height);
    }

    public override Texture? ColorTexture => _color;

    public override Texture? DepthTexture => _depth;

    // For the shared handle and its keyed mutex.
    internal D3D11Texture? Color => _color;

    public override void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == Width && height == Height) return;
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Render target size must be positive; got {width}x{height}.");

        // Checked before any view is released.
        if (Desc.Sharing != RenderTargetSharing.None)
        {
            throw new InvalidOperationException(
                $"A shared render target cannot be resized in place ({Width}x{Height} to {width}x{height}): " +
                "the consumer imported its NT handle. Recreate it under a new generation and retire the old one.");
        }

        ReleaseViews();
        // Storage is swapped inside the existing Texture objects, so materials
        // sampling this target stay valid.
        _color?.ReplaceStorage(_device, width, height);
        _depth?.ReplaceDepthStorage(_device, width, height);
        Allocate(width, height);
    }

    private void Allocate(int width, int height)
    {
        var dev = (ID3D11Device*)_device.Handle;

        if (_color is not null)
        {
            ID3D11RenderTargetView* rtv = null;
            var rtvDesc = new RenderTargetViewDesc
            {
                // Differs from the resource format on a shared attachment: the
                // resource is UNORM and the sRGB encode is on this view.
                Format = _color.RtvFormat,
                ViewDimension = RtvDimension.Texture2D,
            };
            rtvDesc.Anonymous.Texture2D = new Tex2DRtv { MipSlice = 0 };
            SilkMarshal.ThrowHResult(dev->CreateRenderTargetView(_color.Resource, &rtvDesc, &rtv));
            _rtv = ComOwnership.Own(rtv);
        }

        if (_depth is not null)
        {
            // Explicit desc: the resource is R32_TYPELESS, which a DSV cannot use.
            var dsvDesc = new DepthStencilViewDesc
            {
                Format = Silk.NET.DXGI.Format.FormatD32Float,
                ViewDimension = DsvDimension.Texture2D,
            };
            dsvDesc.Anonymous.Texture2D = new Tex2DDsv { MipSlice = 0 };

            ID3D11DepthStencilView* dsv = null;
            SilkMarshal.ThrowHResult(dev->CreateDepthStencilView(_depth.Resource, &dsvDesc, &dsv));
            _dsv = ComOwnership.Own(dsv);
        }

        Width = width;
        Height = height;
    }

    private void ReleaseViews()
    {
        // Release, not Dispose: this runs on resize and again on Dispose.
        ComOwnership.Release(ref _dsv);
        ComOwnership.Release(ref _rtv);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReleaseViews();
        _color?.Dispose();
        _depth?.Dispose();
    }
}
