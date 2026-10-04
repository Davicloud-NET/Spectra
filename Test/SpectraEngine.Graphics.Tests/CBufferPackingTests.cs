using System;
using System.Runtime.CompilerServices;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace SpectraEngine.Graphics.Tests;

// A C# array whose stride differs from the shader's uploads without error and
// reads garbage from element one on, on D3D only. GL packs arrays tightly.
/// <summary>
/// HLSL constant buffer packing, measured by compiling a buffer and
/// reflecting it. Needs no device.
/// </summary>
public sealed unsafe class CBufferPackingTests
{
    private const string Source = """
        cbuffer Packing : register(b0)
        {
            float    scalars[8];
            float3   positions[8];
            float4   colors[8];
            float4x4 cascades[2];
            float3   loose;
        };

        float4 main() : SV_Target0
        {
            return colors[0] + float4(positions[0], scalars[0]) + mul(cascades[0], float4(loose, 1));
        }
        """;

    [Theory]
    // name, count, C# element stride, HLSL element stride
    [InlineData("scalars", 8, 4, 16)]     // float:    C# 4 bytes,  HLSL 16. mismatch.
    [InlineData("positions", 8, 12, 16)]  // float3:   C# 12 bytes, HLSL 16. mismatch.
    [InlineData("colors", 8, 16, 16)]     // float4:   agree.
    [InlineData("cascades", 2, 64, 64)]   // float4x4: agree.
    public void An_array_members_hlsl_stride_is_what_it_is(
        string name, int count, int managedStride, int expectedHlslStride)
    {
        Member member = Reflect(name);

        member.Elements.ShouldBe((uint)count);

        // The last element is not padded out.
        int stride = expectedHlslStride;
        int tail = managedStride;
        ((int)member.Size).ShouldBe((count - 1) * stride + tail,
            $"{name} should occupy {count - 1} strides of {stride} plus a {tail}-byte tail");

        bool safeForBulkCopy = managedStride == expectedHlslStride;
        (member.Size == count * managedStride).ShouldBe(safeForBulkCopy);
    }

    [Fact]
    public void Only_vec4_and_mat4_arrays_can_be_bulk_copied()
    {
        // Why SetUniform has no float or vec3 array overloads.
        Reflect("colors").Size.ShouldBe(8u * 16u);
        Reflect("cascades").Size.ShouldBe(2u * 64u);

        Reflect("scalars").Size.ShouldNotBe(8u * 4u);
        Reflect("positions").Size.ShouldNotBe(8u * 12u);
    }

    [Fact]
    public void Array_members_start_on_a_sixteen_byte_boundary()
    {
        (Reflect("scalars").Offset % 16).ShouldBe(0u);
        (Reflect("positions").Offset % 16).ShouldBe(0u);
        (Reflect("colors").Offset % 16).ShouldBe(0u);
        (Reflect("cascades").Offset % 16).ShouldBe(0u);
    }

    [Fact]
    public void Matrices_are_column_major_which_is_the_engine_s_unwritten_contract()
    {
        // The engine uploads row-major System.Numerics matrices untransposed.
        // That only works while fxc packs column-major, its default.
        // D3DCOMPILE_PACK_MATRIX_ROW_MAJOR, row_major or DXC -Zpr would
        // transpose every matrix with no error.
        Reflect("cascades").Class.ShouldBe(D3DShaderVariableClass.D3DSvcMatrixColumns);
    }

    private readonly record struct Member(uint Offset, uint Size, uint Elements, D3DShaderVariableClass Class);

    private static Member Reflect(string name)
    {
        using var compiler = D3DCompiler.GetApi();
        byte[] bytecode = Compile(compiler);

        ID3D11ShaderReflection* refl = null;
        Guid iid = ID3D11ShaderReflection.Guid;
        fixed (byte* p = bytecode)
            SilkMarshal.ThrowHResult(compiler.Reflect(p, (nuint)bytecode.Length, &iid, (void**)&refl));

        try
        {
            ID3D11ShaderReflectionConstantBuffer* cb = refl->GetConstantBufferByIndex(0);
            ID3D11ShaderReflectionVariable* variable = cb->GetVariableByName(name);

            ShaderVariableDesc varDesc = default;
            SilkMarshal.ThrowHResult(variable->GetDesc(&varDesc));

            ID3D11ShaderReflectionType* type = variable->GetType();
            ShaderTypeDesc typeDesc = default;
            SilkMarshal.ThrowHResult(type->GetDesc(&typeDesc));

            return new Member(varDesc.StartOffset, varDesc.Size, typeDesc.Elements, typeDesc.Class);
        }
        finally
        {
            refl->Release();
        }
    }

    private static byte[] Compile(D3DCompiler compiler)
    {
        byte[] source = Encoding.ASCII.GetBytes(Source);
        ComPtr<ID3D10Blob> code = default;
        ComPtr<ID3D10Blob> errors = default;

        fixed (byte* pSrc = source)
        fixed (byte* pProfile = "ps_5_0\0"u8)
        fixed (byte* pEntry = "main\0"u8)
        fixed (byte* pName = "packing\0"u8)
        {
            // Flags 0, 0: what the engine passes.
            int hr = compiler.Compile(
                pSrc, (nuint)source.Length, pName, null,
                ref Unsafe.NullRef<ID3DInclude>(), pEntry, pProfile, 0u, 0u,
                ref code, ref errors);

            if (hr < 0)
            {
                string message = errors.Handle is null
                    ? "(no error blob)"
                    : Encoding.ASCII.GetString((byte*)errors.GetBufferPointer(), (int)errors.GetBufferSize());
                throw new InvalidOperationException($"HLSL compile failed ({hr:X}): {message}");
            }
        }

        var bytes = new byte[(int)code.GetBufferSize()];
        new ReadOnlySpan<byte>(code.GetBufferPointer(), bytes.Length).CopyTo(bytes);
        code.Dispose();
        errors.Dispose();
        return bytes;
    }
}
