namespace SpectraEngine.Core.Graphics;

/// <summary>CPU-prepared mip chain, produced without a graphics context.</summary>
internal sealed record PreparedTextureData(TextureFormat Format, byte[] Payload, TextureMipDesc[] Mips);
