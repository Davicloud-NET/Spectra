namespace SpectraEngine.Core.Graphics;

// Mip chain prepared on the CPU, no graphics context needed.
internal sealed record PreparedTextureData(TextureFormat Format, byte[] Payload, TextureMipDesc[] Mips);
