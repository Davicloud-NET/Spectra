using System;
using System.Runtime.CompilerServices;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Compiles the built-in shaders to D3D bytecode. D3DCompile is a library
/// call, so this needs no device.
/// </summary>
public sealed unsafe class BaseShaderHlslCompilationTests
{
    public static TheoryData<string> AllBaseShaders() =>
    [
        nameof(BaseShaders.Lit),
        nameof(BaseShaders.DebugLine),
        nameof(BaseShaders.PostResolve),
        nameof(BaseShaders.GBufferFill),
        nameof(BaseShaders.DeferredLight),
    ];

    [Theory]
    [MemberData(nameof(AllBaseShaders))]
    public void Compiles_to_d3d_bytecode(string shaderName)
    {
        string source = shaderName switch
        {
            nameof(BaseShaders.Lit) => BaseShaders.Lit,
            nameof(BaseShaders.DebugLine) => BaseShaders.DebugLine,
            nameof(BaseShaders.PostResolve) => BaseShaders.PostResolve,
            nameof(BaseShaders.GBufferFill) => BaseShaders.GBufferFill,
            nameof(BaseShaders.DeferredLight) => BaseShaders.DeferredLight,
            _ => throw new ArgumentOutOfRangeException(nameof(shaderName)),
        };

        ReadOnlySpan<GraphicsBackend> targets = [GraphicsBackend.D3D11];
        CompiledShaderFile compiled = new SpectraShadeCompiler().Compile(source, targets);
        PipelineBlob blob = compiled.GetPipeline(GraphicsBackend.D3D11).ShouldNotBeNull();

        using var compiler = D3DCompiler.GetApi();

        Compile(compiler, Encoding.ASCII.GetString(blob.VertexData), "vs_5_0", shaderName);
        Compile(compiler, Encoding.ASCII.GetString(blob.FragmentData), "ps_5_0", shaderName);

        // The generated instanced stage, where the shader has one.
        if (blob.InstancedVertexData is { } instanced)
            Compile(compiler, Encoding.ASCII.GetString(instanced), "vs_5_0", shaderName + " (instanced)");
    }

    private static void Compile(D3DCompiler compiler, string hlsl, string profile, string label)
    {
        byte[] source = Encoding.ASCII.GetBytes(hlsl);
        ComPtr<ID3D10Blob> code = default;
        ComPtr<ID3D10Blob> errors = default;

        fixed (byte* pSrc = source)
        fixed (byte* pProfile = Encoding.ASCII.GetBytes(profile + "\0"))
        fixed (byte* pEntry = "main\0"u8)
        fixed (byte* pName = Encoding.ASCII.GetBytes(label + "\0"))
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
                errors.Dispose();
                code.Dispose();
                throw new InvalidOperationException(
                    $"{label} {profile} failed to compile ({hr:X}):{Environment.NewLine}{message}" +
                    $"{Environment.NewLine}--- generated HLSL ---{Environment.NewLine}{hlsl}");
            }
        }

        code.GetBufferSize().ShouldBeGreaterThan(0u);
        code.Dispose();
        errors.Dispose();
    }
}
