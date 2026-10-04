using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using System;

namespace SpectraEngine.Core.Graphics.D3D12;

// Tracks its own resource states and emits a barrier only on a real change.
// A resize rewrites the SRV into the same heap slot, so materials that sample
// the target keep working.
internal sealed unsafe class D3D12RenderTarget : RenderTarget
{
    private readonly D3D12Renderer _renderer;
    private readonly D3D12Texture? _color;
    private readonly D3D12Texture? _depth;
    private ComPtr<ID3D12DescriptorHeap> _rtvHeap;
    private ComPtr<ID3D12DescriptorHeap> _dsvHeap;
    private bool _disposed;

    internal CpuDescriptorHandle Rtv { get; private set; }
    internal CpuDescriptorHandle Dsv { get; private set; }
    internal bool HasDepth => Desc.Depth;

    internal ResourceStates ColorState { get; private set; } = ResourceStates.PixelShaderResource;

    internal D3D12RenderTarget(D3D12Renderer renderer, in RenderTargetDesc desc)
    {
        desc.Validate();

        _renderer = renderer;
        Desc = desc;

        if (desc.Color)
        {
            _color = D3D12Texture.CreateRenderTargetTexture(
                renderer, desc.Width, desc.Height, desc.ColorFormat, desc.ColorSpace, desc.Filter, desc.Wrap);
        }

        if (desc.Depth)
            _depth = D3D12Texture.CreateDepthTexture(renderer, desc.Width, desc.Height);

        if (desc.Color)
        {
            _rtvHeap = renderer.CreateDescriptorHeap(DescriptorHeapType.Rtv, 1, shaderVisible: false);
            Rtv = ((ID3D12DescriptorHeap*)_rtvHeap.Handle)->GetCPUDescriptorHandleForHeapStart();
        }

        if (desc.Depth)
        {
            _dsvHeap = renderer.CreateDescriptorHeap(DescriptorHeapType.Dsv, 1, shaderVisible: false);
            Dsv = ((ID3D12DescriptorHeap*)_dsvHeap.Handle)->GetCPUDescriptorHandleForHeapStart();
        }

        Allocate(desc.Width, desc.Height);
    }

    public override Texture? ColorTexture => _color;

    public override Texture? DepthTexture => _depth;

    // Rests in DepthWrite: every geometry pass writes it and few things sample it.
    internal ResourceStates DepthState { get; private set; } = ResourceStates.DepthWrite;

    internal Format ColorFormat => _color?.DxgiFormat ?? Format.FormatUnknown;

    internal bool HasColor => _color is not null;

    // The DSV format a PSO must name. The resource is typeless, and the back
    // buffer's depth is D24S8, not this.
    internal Format DepthViewFormat =>
        HasDepth ? Format.FormatD32Float : Format.FormatUnknown;

    public override void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == Width && height == Height) return;
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Render target size must be positive; got {width}x{height}.");

        // The GPU must be done with the old resource before it is released.
        _renderer.WaitForGpu();

        _color?.ReplaceStorage(_renderer, width, height);
        _depth?.ReplaceDepthStorage(_renderer, width, height);
        ColorState = ResourceStates.PixelShaderResource;
        DepthState = ResourceStates.DepthWrite;
        Allocate(width, height);
    }

    private void Allocate(int width, int height)
    {
        if (_color is not null)
        {
            var rtvDesc = new RenderTargetViewDesc
            {
                Format = _color.DxgiFormat,
                ViewDimension = RtvDimension.Texture2D,
            };
            rtvDesc.Anonymous.Texture2D = new Tex2DRtv { MipSlice = 0, PlaneSlice = 0 };
            _renderer.DevicePtr->CreateRenderTargetView(_color.Resource, &rtvDesc, Rtv);
        }

        if (_depth is not null)
        {
            // Explicit desc: the resource is typeless, so a null desc has no depth format.
            var dsvDesc = new DepthStencilViewDesc
            {
                Format = Format.FormatD32Float,
                ViewDimension = DsvDimension.Texture2D,
            };
            dsvDesc.Anonymous.Texture2D = new Tex2DDsv { MipSlice = 0 };

            _renderer.DevicePtr->CreateDepthStencilView(_depth.Resource, &dsvDesc, Dsv);
        }

        Width = width;
        Height = height;
    }

    internal void TransitionColor(ID3D12GraphicsCommandList* list, ResourceStates state)
    {
        if (_color is null || ColorState == state) return;

        D3D12Renderer.Transition(list, _color.Resource, ColorState, state);
        ColorState = state;
    }

    internal void TransitionDepth(ID3D12GraphicsCommandList* list, ResourceStates state)
    {
        if (_depth is null || DepthState == state) return;

        D3D12Renderer.Transition(list, _depth.Resource, DepthState, state);
        DepthState = state;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _renderer.Retire(ref _dsvHeap);
        _renderer.Retire(ref _rtvHeap);
        _color?.Dispose();
        _depth?.Dispose();
    }
}
