using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Graphics.Tests;

// Hand-assembled BC7 mode 6 blocks: with both endpoints equal, every index
// decodes to the endpoint, so the colours are known without an encoder.
internal static class Bc7Fixture
{
    internal const int BlockBytes = 16;

    // Mode 6 endpoints are 7 bits plus one parity bit shared by all four
    // channels. 255 and 1 are both odd, so one p-bit reproduces them to the byte.
    internal const byte On = 255;

    internal const byte Off = 1;

    // A mode 6 block of one flat colour. All four channels must share a low bit.
    internal static byte[] SolidBlock(byte r, byte g, byte b, byte a)
    {
        var block = new byte[BlockBytes];
        int bit = 0;

        void Put(uint value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (((value >> i) & 1u) != 0)
                    block[bit >> 3] |= (byte)(1 << (bit & 7));
                bit++;
            }
        }

        // Mode is unary: six zeros then a one.
        Put(0b100_0000u, 7);

        Put((uint)(r >> 1), 7);
        Put((uint)(r >> 1), 7);
        Put((uint)(g >> 1), 7);
        Put((uint)(g >> 1), 7);
        Put((uint)(b >> 1), 7);
        Put((uint)(b >> 1), 7);
        Put((uint)(a >> 1), 7);
        Put((uint)(a >> 1), 7);

        uint parity = (uint)(r & 1);
        Put(parity, 1);
        Put(parity, 1);

        // The 63 index bits stay zero.
        return block;
    }

    // Quadrants are named as the picture is seen.
    internal static byte[] TopLeftBlock => SolidBlock(On, Off, Off, On);

    internal static byte[] TopRightBlock => SolidBlock(Off, On, Off, On);

    internal static byte[] BottomLeftBlock => SolidBlock(Off, Off, On, On);

    internal static byte[] BottomRightBlock => SolidBlock(On, On, Off, On);

    // Mip 1 is white, which no quadrant is.
    internal static byte[] SecondLevelBlock => SolidBlock(On, On, On, On);

    internal const int BaseSize = 16;

    internal const int SecondSize = 8;

    // Quadrants at 16x16, flat white at 8x8. Block row 0 is the bottom of the
    // picture, like texel row 0 on the uncompressed path.
    // padded: row pitch wider than the data, which a cooked file may carry.
    internal static byte[] BuildTwoLevelPayload(bool padded, out TextureMipDesc[] mips)
    {
        const int baseBlocks = BaseSize / 4;      // 4 across, 4 down
        const int secondBlocks = SecondSize / 4;  // 2 across, 2 down

        // 256 is D3D12's copy alignment.
        int basePitch = padded ? 256 : baseBlocks * BlockBytes;
        int secondPitch = padded ? 256 : secondBlocks * BlockBytes;

        int baseBytes = basePitch * baseBlocks;
        int secondBytes = secondPitch * secondBlocks;

        mips =
        [
            new TextureMipDesc(BaseSize, BaseSize, 0, basePitch),
            new TextureMipDesc(SecondSize, SecondSize, baseBytes, secondPitch),
        ];

        var payload = new byte[baseBytes + secondBytes];

        for (int blockRow = 0; blockRow < baseBlocks; blockRow++)
        {
            bool upperHalf = blockRow >= baseBlocks / 2;
            for (int blockColumn = 0; blockColumn < baseBlocks; blockColumn++)
            {
                bool rightHalf = blockColumn >= baseBlocks / 2;
                byte[] block = upperHalf
                    ? (rightHalf ? TopRightBlock : TopLeftBlock)
                    : (rightHalf ? BottomRightBlock : BottomLeftBlock);
                block.CopyTo(payload.AsSpan(blockRow * basePitch + blockColumn * BlockBytes));
            }
        }

        for (int blockRow = 0; blockRow < secondBlocks; blockRow++)
        {
            for (int blockColumn = 0; blockColumn < secondBlocks; blockColumn++)
            {
                SecondLevelBlock.CopyTo(
                    payload.AsSpan(baseBytes + blockRow * secondPitch + blockColumn * BlockBytes));
            }
        }

        return payload;
    }
}
