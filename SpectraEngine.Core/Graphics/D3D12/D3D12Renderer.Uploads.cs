using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace SpectraEngine.Core.Graphics.D3D12;

public sealed unsafe partial class D3D12Renderer
{
    private bool _assetOnlyRecording;

    internal override PreparedTextureData? PrepareTextureUpload(in TextureUploadDesc desc)
    {
        if (desc.Filter != TextureFilter.LinearMipmap || desc.HasSuppliedMipChain || TextureFormatInfo.IsBlockCompressed(desc.Format)) return null;
        var levels = D3D12Texture.BuildMipChain(TextureUploadLayout.TightLevel(desc.Payload, desc.Format, desc.Mips[0], out _).ToArray(),
            desc.Width, desc.Height, TextureFormatInfo.BytesPerBlock(desc.Format),
            TextureFormatInfo.Resolve(desc.Format, desc.ColorSpace) == TextureColorSpace.Srgb);
        byte[] bytes = TextureUploadLayout.Flatten(desc.Format, levels, out var mips);
        return new(desc.Format, bytes, mips);
    }

    private void EnsureUploadRecording()
    {
        if (_isRecording) return;
        BeginRecording();
        _assetOnlyRecording = true;
    }

    internal void UploadTextureRows(D3D12Texture texture, int level, TextureMipDesc mip,
        int firstRow, int rowCount, ReadOnlySpan<byte> bytes)
    {
        EnsureUploadRecording();
        int tight = texture.Format == TextureFormat.Rgb8 ? checked(mip.Width * 4)
            : TextureFormatInfo.TightRowPitch(texture.Format, mip.Width);
        uint pitch = checked((uint)((tight + 255L) / 256 * 256));
        var slice = AllocUpload(checked(pitch * (uint)rowCount), 512);
        TextureUploadLayout.CopyRows(bytes, mip.RowPitch, new Span<byte>(slice.Cpu, checked((int)pitch * rowCount)),
            (int)pitch, mip.Width, rowCount, texture.Format);
        uint blockHeight = TextureFormatInfo.IsBlockCompressed(texture.Format) ? 4u : 1u;
        uint y = (uint)firstRow * blockHeight;
        uint height = Math.Min((uint)mip.Height - y, (uint)rowCount * blockHeight);
        var source = new TextureCopyLocation { PResource = (ID3D12Resource*)_uploadRing.Handle, Type = TextureCopyType.PlacedFootprint };
        source.Anonymous.PlacedFootprint = new PlacedSubresourceFootprint
        {
            Offset = slice.GpuVa - _uploadRingGpuVa,
            Footprint = new SubresourceFootprint(texture.DxgiFormat, (uint)mip.Width, height, 1, pitch),
        };
        var destination = new TextureCopyLocation { PResource = texture.Resource, Type = TextureCopyType.SubresourceIndex };
        destination.Anonymous.SubresourceIndex = (uint)level;
        ((ID3D12GraphicsCommandList*)_commandList.Handle)->CopyTextureRegion(&destination, 0, y, 0, &source, null);
    }

    internal void FinishTextureUpload(D3D12Texture texture)
    {
        EnsureUploadRecording();
        Transition((ID3D12GraphicsCommandList*)_commandList.Handle, texture.Resource,
            ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
    }

    public override void FlushUploads(bool waitForCompletion = false)
    {
        if (_assetOnlyRecording && _isRecording)
        {
            _assetOnlyRecording = false;
            var list = (ID3D12GraphicsCommandList*)_commandList.Handle;
            SilkMarshal.ThrowHResult(list->Close());
            _isRecording = false;
            ID3D12CommandList* commands = (ID3D12CommandList*)list;
            ((ID3D12CommandQueue*)_queue.Handle)->ExecuteCommandLists(1, &commands);
            SignalSubmission();
        }
        if (waitForCompletion) WaitForGpu();
    }
}
