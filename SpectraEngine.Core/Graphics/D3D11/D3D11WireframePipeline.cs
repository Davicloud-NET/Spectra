using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace SpectraEngine.Core.Graphics.D3D11;

/// <summary>
/// Draws the same list as <see cref="D3D11ForwardPipeline"/> in wireframe.
/// </summary>
public sealed unsafe class D3D11WireframePipeline : ID3D11RenderPipeline
{
    private D3D11Renderer? _renderer;
    private ComPtr<ID3D11RasterizerState> _wireframeState;
    private ComPtr<ID3D11RasterizerState> _solidState;

    public string Name => "Wireframe";

    /// <summary>Ambient light level, added to every surface.</summary>
    public float Ambient { get; set; } = 0.05f;

    public void Initialize(D3D11Renderer renderer)
    {
        _renderer = renderer;

        var dev = (ID3D11Device*)renderer.Device.Handle;

        var wireDesc = new RasterizerDesc
        {
            FillMode = FillMode.Wireframe,
            CullMode = CullMode.None,
            FrontCounterClockwise = 1,
            DepthClipEnable = 1,
        };
        ID3D11RasterizerState* wire = null;
        SilkMarshal.ThrowHResult(dev->CreateRasterizerState(&wireDesc, &wire));
        _wireframeState = ComOwnership.Own(wire);

        var solidDesc = new RasterizerDesc
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.Back,
            FrontCounterClockwise = 1,
            DepthClipEnable = 1,
        };
        ID3D11RasterizerState* solid = null;
        SilkMarshal.ThrowHResult(dev->CreateRasterizerState(&solidDesc, &solid));
        _solidState = ComOwnership.Own(solid);
    }

    public void Execute(in D3D11RenderContext context)
    {
        var ctx = (ID3D11DeviceContext*)context.Context.Handle;

        // Before the pass: this may create a shader program.
        context.Renderer.PrepareWorldLines(gbuffer: false);

        context.Renderer.BeginPass(context.Renderer.FrameTarget, PassClear.To(ClearColors.Wireframe));
        try
        {
            if (context.Scene is null) return;

            var camera = context.Scene.Camera;
            // The pass's aspect, not the window's: the target may be offscreen.
            if (context.Renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            ctx->RSSetState((ID3D11RasterizerState*)_wireframeState.Handle);
            context.Renderer.DrawLit(context.View, camera, Ambient, shadowLightIndex: -1);
            ctx->RSSetState((ID3D11RasterizerState*)_solidState.Handle);

            // Inside the pass: world lines are tested against the scene's depth.
            context.Renderer.FlushWorldLines(camera);
        }
        finally
        {
            context.Renderer.EndPass();
        }
    }

    public void Dispose()
    {
        _wireframeState.Dispose();
        _solidState.Dispose();
    }
}
