using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;
using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SpectraEngine.Core.Graphics.D3D11;

// A VS+PS pair. Uniforms are staged in CPU shadows of each cbuffer and
// uploaded by Use(). Layout comes from shader reflection.
internal sealed unsafe class D3D11ShaderProgram : ShaderProgram
{
    private readonly D3DCompiler _compiler;
    private readonly ComPtr<ID3D11Device> _device;
    private readonly ComPtr<ID3D11DeviceContext> _context;

    private ComPtr<ID3D11VertexShader> _vs;
    private ComPtr<ID3D11PixelShader> _ps;
    private byte[] _vsBytecode;

    private CBufferSlot[] _cbuffers = Array.Empty<CBufferSlot>();

    private Dictionary<string, UniformLocation> _uniforms = new(StringComparer.Ordinal);

    // SpectraShade emits the SRV and its sampler at the same register, so one
    // slot number serves both.
    private Dictionary<string, uint> _textureSlots = new(StringComparer.Ordinal);

    // The renderer's cache, not ours: it resets it when the context's SRV slots are cleared.
    private readonly D3D11BindCache _bindCache;

    private bool _disposed;

    // Input layouts are created against this.
    public ReadOnlyMemory<byte> VertexBytecode => _vsBytecode;

    private D3D11ShaderProgram(
        D3DCompiler compiler,
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        D3D11BindCache bindCache,
        ComPtr<ID3D11VertexShader> vs,
        ComPtr<ID3D11PixelShader> ps,
        byte[] vsBytecode,
        byte[] psBytecode)
    {
        _compiler = compiler;
        _device = device;
        _context = context;
        _bindCache = bindCache;
        _vs = vs;
        _ps = ps;
        _vsBytecode = vsBytecode;
        BuildReflection(psBytecode);
    }

    internal static D3D11ShaderProgram Create(
        D3DCompiler compiler,
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        D3D11BindCache bindCache,
        string vertexSource,
        string fragmentSource)
    {
        byte[] vsBlob = CompileStage(compiler, vertexSource, "vs_5_0", "Vertex");
        byte[] psBlob = CompileStage(compiler, fragmentSource, "ps_5_0", "Fragment");

        ID3D11VertexShader* vsPtr = null;
        ID3D11PixelShader* psPtr = null;
        var dev = (ID3D11Device*)device.Handle;
        fixed (byte* pVs = vsBlob)
        fixed (byte* pPs = psBlob)
        {
            SilkMarshal.ThrowHResult(dev->CreateVertexShader(pVs, (nuint)vsBlob.Length, null, &vsPtr));
            SilkMarshal.ThrowHResult(dev->CreatePixelShader(pPs, (nuint)psBlob.Length, null, &psPtr));
        }

        return new D3D11ShaderProgram(compiler, device, context, bindCache,
            ComOwnership.Own(vsPtr),
            ComOwnership.Own(psPtr),
            vsBlob, psBlob);
    }

    public override bool TryReload(PipelineBlob blob, [NotNullWhen(false)] out string? error)
    {
        if (blob.Backend != GraphicsBackend.D3D11) { error = $"Expected D3D11 blob, got {blob.Backend}"; return false; }
        if (blob.Format != ShaderDataFormat.SourceText) { error = $"D3D11 requires SourceText, got {blob.Format}"; return false; }

        string vsSrc = Encoding.UTF8.GetString(blob.VertexData
            ?? throw new InvalidOperationException("Reload blob has no vertex stage"));
        string psSrc = Encoding.UTF8.GetString(blob.FragmentData
            ?? throw new InvalidOperationException("Reload blob has no fragment stage"));

        byte[] vsBlob, psBlob;
        try
        {
            vsBlob = CompileStage(_compiler, vsSrc, "vs_5_0", "Vertex");
            psBlob = CompileStage(_compiler, psSrc, "ps_5_0", "Fragment");
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }

        ID3D11VertexShader* newVsPtr = null;
        ID3D11PixelShader* newPsPtr = null;
        try
        {
            var dev = (ID3D11Device*)_device.Handle;
            fixed (byte* pVs = vsBlob)
            fixed (byte* pPs = psBlob)
            {
                SilkMarshal.ThrowHResult(dev->CreateVertexShader(pVs, (nuint)vsBlob.Length, null, &newVsPtr));
                SilkMarshal.ThrowHResult(dev->CreatePixelShader(pPs, (nuint)psBlob.Length, null, &newPsPtr));
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (newVsPtr is not null) newVsPtr->Release();
            if (newPsPtr is not null) newPsPtr->Release();
            return false;
        }

        var oldVs = _vs;
        var oldPs = _ps;
        DisposeCBuffers();
        _vs = ComOwnership.Own(newVsPtr);
        _ps = ComOwnership.Own(newPsPtr);
        _vsBytecode = vsBlob;
        BuildReflection(psBlob);
        oldVs.Dispose();
        oldPs.Dispose();

        error = null;
        return true;
    }

    private static byte[] CompileStage(D3DCompiler compiler, string source, string profile, string stageLabel)
    {
        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        ComPtr<ID3D10Blob> code = default;
        ComPtr<ID3D10Blob> errors = default;

        fixed (byte* pSrc = sourceBytes)
        fixed (byte* pProfile = Encoding.ASCII.GetBytes(profile + "\0"))
        fixed (byte* pEntry = "main\0"u8)
        fixed (byte* pName = Encoding.ASCII.GetBytes(stageLabel + "\0"))
        {
            int hr = compiler.Compile(
                pSrc,
                (nuint)sourceBytes.Length,
                pName,
                null,
                ref Unsafe.NullRef<ID3DInclude>(),
                pEntry,
                pProfile,
                0u,
                0u,
                ref code,
                ref errors);

            if (hr < 0)
            {
                string msg = ReadBlobString(errors);
                errors.Dispose();
                code.Dispose();
                throw new InvalidOperationException(
                    $"{stageLabel} HLSL compile failed ({hr:X}): {msg}");
            }
        }

        byte[] bytecode = ReadBlobBytes(code);
        code.Dispose();
        errors.Dispose();
        return bytecode;
    }

    private static byte[] ReadBlobBytes(ComPtr<ID3D10Blob> blob)
    {
        if (blob.Handle is null) return Array.Empty<byte>();
        nuint len = blob.GetBufferSize();
        void* ptr = blob.GetBufferPointer();
        var bytes = new byte[(int)len];
        new ReadOnlySpan<byte>(ptr, (int)len).CopyTo(bytes);
        return bytes;
    }

    private static string ReadBlobString(ComPtr<ID3D10Blob> blob)
    {
        if (blob.Handle is null) return "(no error blob)";
        nuint len = blob.GetBufferSize();
        void* ptr = blob.GetBufferPointer();
        return Encoding.UTF8.GetString((byte*)ptr, (int)len).TrimEnd('\0', '\n', '\r');
    }

    private void BuildReflection(byte[] psBytecode)
    {
        _uniforms = new Dictionary<string, UniformLocation>(StringComparer.Ordinal);
        _textureSlots = new Dictionary<string, uint>(StringComparer.Ordinal);
        var cbuffers = new List<CBufferSlot>();

        ReflectStage(_vsBytecode, cbuffers, isPs: false);
        ReflectStage(psBytecode, cbuffers, isPs: true);

        _cbuffers = cbuffers.ToArray();
    }

    private void ReflectStage(byte[] bytecode, List<CBufferSlot> cbuffers, bool isPs)
    {
        ID3D11ShaderReflection* reflPtr = null;
        Guid iid = ID3D11ShaderReflection.Guid;
        fixed (byte* pBytecode = bytecode)
        {
            SilkMarshal.ThrowHResult(_compiler.Reflect(
                pBytecode,
                (nuint)bytecode.Length,
                &iid,
                (void**)&reflPtr));
        }

        ShaderDesc shaderDesc = default;
        SilkMarshal.ThrowHResult(reflPtr->GetDesc(&shaderDesc));

        // Indexed by declaration order, not register slot.
        for (uint i = 0; i < shaderDesc.ConstantBuffers; i++)
        {
            ID3D11ShaderReflectionConstantBuffer* cb = reflPtr->GetConstantBufferByIndex(i);
            ShaderBufferDesc bufDesc = default;
            SilkMarshal.ThrowHResult(cb->GetDesc(&bufDesc));

            string name = MarshalUtf8(bufDesc.Name);
            ShaderInputBindDesc bindDesc = default;
            SilkMarshal.ThrowHResult(reflPtr->GetResourceBindingDescByName(bufDesc.Name, &bindDesc));
            uint slot = bindDesc.BindPoint;

            // Already seen in the other stage: share its buffer.
            int existing = cbuffers.FindIndex(c => c.Slot == slot && c.Name == name);
            if (existing >= 0)
            {
                cbuffers[existing] = cbuffers[existing] with
                {
                    UsedInVs = cbuffers[existing].UsedInVs || !isPs,
                    UsedInPs = cbuffers[existing].UsedInPs || isPs,
                };
                continue;
            }

            uint sizeBytes = bufDesc.Size;
            uint roundedSize = ((sizeBytes + 15u) / 16u) * 16u;

            var buffer = CreateCBuffer(_device, roundedSize);
            byte[] shadow = new byte[roundedSize];

            var slotInfo = new CBufferSlot(name, slot, buffer, shadow, dirty: true, usedInVs: !isPs, usedInPs: isPs);
            cbuffers.Add(slotInfo);
            int newIdx = cbuffers.Count - 1;

            for (uint v = 0; v < bufDesc.Variables; v++)
            {
                ID3D11ShaderReflectionVariable* variable = cb->GetVariableByIndex(v);
                ShaderVariableDesc varDesc = default;
                SilkMarshal.ThrowHResult(variable->GetDesc(&varDesc));
                string varName = MarshalUtf8(varDesc.Name);
                _uniforms[varName] = new UniformLocation(newIdx, varDesc.StartOffset, varDesc.Size);
            }
        }

        // Textures only: the sampler sits at the same register.
        for (uint i = 0; i < shaderDesc.BoundResources; i++)
        {
            ShaderInputBindDesc bind = default;
            SilkMarshal.ThrowHResult(reflPtr->GetResourceBindingDesc(i, &bind));
            if (bind.Type != D3DShaderInputType.D3DSitTexture) continue;
            string name = MarshalUtf8(bind.Name);
            _textureSlots[name] = bind.BindPoint;
        }

        reflPtr->Release();
    }

    private static ComPtr<ID3D11Buffer> CreateCBuffer(ComPtr<ID3D11Device> device, uint sizeBytes)
    {
        var desc = new BufferDesc
        {
            ByteWidth = sizeBytes,
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
            MiscFlags = 0,
            StructureByteStride = 0,
        };
        ID3D11Buffer* bufPtr = null;
        SilkMarshal.ThrowHResult(((ID3D11Device*)device.Handle)->CreateBuffer(&desc, null, &bufPtr));
        return ComOwnership.Own(bufPtr);
    }

    private static string MarshalUtf8(byte* ptr)
    {
        if (ptr is null) return string.Empty;
        int len = 0;
        while (ptr[len] != 0) len++;
        return Encoding.UTF8.GetString(ptr, len);
    }

    public override void Use()
    {
        var ctx = (ID3D11DeviceContext*)_context.Handle;

        for (int i = 0; i < _cbuffers.Length; i++)
        {
            ref var slot = ref _cbuffers[i];
            if (slot.Dirty)
            {
                MappedSubresource mapped = default;
                SilkMarshal.ThrowHResult(ctx->Map(
                    (ID3D11Resource*)slot.Buffer.Handle, 0, Map.WriteDiscard, 0, &mapped));
                fixed (byte* src = slot.Shadow)
                {
                    System.Buffer.MemoryCopy(src, mapped.PData, slot.Shadow.Length, slot.Shadow.Length);
                }
                ctx->Unmap((ID3D11Resource*)slot.Buffer.Handle, 0);
                slot.Dirty = false;
            }
        }

        ctx->VSSetShader((ID3D11VertexShader*)_vs.Handle, null, 0);
        ctx->PSSetShader((ID3D11PixelShader*)_ps.Handle, null, 0);

        for (int i = 0; i < _cbuffers.Length; i++)
        {
            ref var slot = ref _cbuffers[i];
            ID3D11Buffer* buf = (ID3D11Buffer*)slot.Buffer.Handle;
            if (slot.UsedInVs) ctx->VSSetConstantBuffers(slot.Slot, 1, &buf);
            if (slot.UsedInPs) ctx->PSSetConstantBuffers(slot.Slot, 1, &buf);
        }
    }

    public override void SetUniform(string name, Matrix4x4 value) => Write(name, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
    public override void SetUniform(string name, Vector4 value) => Write(name, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
    public override void SetUniform(string name, Vector3 value)
    {
        // Write clips this to the reflected size, 12 bytes for a float3.
        Span<float> tmp = stackalloc float[4] { value.X, value.Y, value.Z, 0f };
        Write(name, MemoryMarshal.AsBytes(tmp));
    }
    public override void SetUniform(string name, Vector2 value) => Write(name, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
    public override void SetUniform(string name, float value) => Write(name, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
    public override void SetUniform(string name, int value) => Write(name, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));

    public override void SetUniform(string name, ReadOnlySpan<Vector4> values)
        => WriteArray(name, MemoryMarshal.AsBytes(values), values.Length, sizeof(float) * 4, "vec4");

    public override void SetUniform(string name, ReadOnlySpan<Matrix4x4> values)
        => WriteArray(name, MemoryMarshal.AsBytes(values), values.Length, sizeof(float) * 16, "mat4");

    // An array uniform must fill the shader's array. Clamping instead would
    // leave a stale tail: ten lights uploaded over sixty still shades with fifty old ones.
    private void WriteArray(string name, ReadOnlySpan<byte> bytes, int count, int stride, string elementType)
    {
        // Unknown names are ignored: pipelines set uniforms a shader may not declare.
        if (!_uniforms.TryGetValue(name, out var loc)) return;
        if (count == 0) return;

        if (bytes.Length != (int)loc.Size)
        {
            throw new ArgumentException(
                $"Uniform '{name}' is {loc.Size} bytes in the shader, but {count} {elementType} " +
                $"element(s) is {bytes.Length}. An array uniform must be filled exactly.",
                nameof(name));
        }

        WriteBytes(loc, bytes);
    }

    private void Write(string name, ReadOnlySpan<byte> bytes)
    {
        if (!_uniforms.TryGetValue(name, out var loc)) return;
        int copyLen = Math.Min((int)loc.Size, bytes.Length);
        WriteBytes(loc, bytes[..copyLen]);
    }

    // Skipping identical writes keeps the cbuffer clean, so Use() can skip the Map.
    private void WriteBytes(UniformLocation loc, ReadOnlySpan<byte> bytes)
    {
        ref var slot = ref _cbuffers[loc.CBufferIndex];
        var target = slot.Shadow.AsSpan((int)loc.Offset, bytes.Length);
        if (bytes.SequenceEqual(target)) return;
        bytes.CopyTo(target);
        slot.Dirty = true;
    }

    public override void SetTexture(string name, int unit, Texture texture)
    {
        if (texture is not D3D11Texture d3dTex)
            throw new ArgumentException($"Texture must be D3D11Texture, got {texture.GetType().Name}", nameof(texture));
        if (!_textureSlots.TryGetValue(name, out uint slot)) return;

        ID3D11ShaderResourceView* srv = (ID3D11ShaderResourceView*)d3dTex.Srv.Handle;
        ID3D11SamplerState* samp = (ID3D11SamplerState*)d3dTex.Sampler.Handle;
        if (!_bindCache.MustBind(slot, (nint)srv, (nint)samp))
            return;

        var ctx = (ID3D11DeviceContext*)_context.Handle;
        ctx->PSSetShaderResources(slot, 1, &srv);
        ctx->PSSetSamplers(slot, 1, &samp);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeCBuffers();
        _ps.Dispose();
        _vs.Dispose();
    }

    private void DisposeCBuffers()
    {
        for (int i = 0; i < _cbuffers.Length; i++)
            _cbuffers[i].Buffer.Dispose();
        _cbuffers = Array.Empty<CBufferSlot>();
    }

    private record struct UniformLocation(int CBufferIndex, uint Offset, uint Size);

    private struct CBufferSlot
    {
        public string Name;
        public uint Slot;
        public ComPtr<ID3D11Buffer> Buffer;
        public byte[] Shadow;
        public bool Dirty;
        public bool UsedInVs;
        public bool UsedInPs;

        public CBufferSlot(string name, uint slot, ComPtr<ID3D11Buffer> buffer, byte[] shadow, bool dirty, bool usedInVs, bool usedInPs)
        {
            Name = name;
            Slot = slot;
            Buffer = buffer;
            Shadow = shadow;
            Dirty = dirty;
            UsedInVs = usedInVs;
            UsedInPs = usedInPs;
        }
    }
}
