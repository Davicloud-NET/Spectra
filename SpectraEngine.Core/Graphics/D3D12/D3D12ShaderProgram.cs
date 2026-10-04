using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpectraEngine.Core.Graphics.D3D12;

// A VS+PS pair with its root signature, cbuffer shadows and PSO cache.
// Uniforms go into per-draw slices of the frame upload ring, so one program
// can draw many times a frame with different constants.
internal sealed unsafe class D3D12ShaderProgram : ShaderProgram
{
    private readonly D3D12Renderer _renderer;
    private readonly D3DCompiler _compiler;

    private ComPtr<ID3D12RootSignature> _rootSignature;
    private byte[] _vsBytecode = Array.Empty<byte>();
    private byte[] _psBytecode = Array.Empty<byte>();

    // One per HLSL cbuffer register. The index is also its root CBV slot.
    private CBufferSlot[] _cbuffers = Array.Empty<CBufferSlot>();
    private Dictionary<string, UniformLocation> _uniforms = new(StringComparer.Ordinal);

    // The HLSL generator emits SRV and sampler at the same register, so one
    // number serves both tables.
    private Dictionary<string, uint> _textureSlots = new(StringComparer.Ordinal);
    private uint _srvCount;

    // Staged by SetTexture, consumed by the next Use().
    private readonly Dictionary<uint, D3D12Texture> _pendingTextures = new();

    // Root parameters: cbuffer CBVs first, then the SRV and sampler tables.
    private int _srvTableParam = -1;
    private int _samplerTableParam = -1;

    private readonly Dictionary<D3D12PsoKey, ComPtr<ID3D12PipelineState>> _psoCache = new();
    private bool _disposed;

    // Descriptors one draw takes from each shader-visible ring.
    internal uint SrvCount => _srvCount;

    public ReadOnlyMemory<byte> VertexBytecode => _vsBytecode;

    internal D3D12ShaderProgram(D3D12Renderer renderer, D3DCompiler compiler, string vertexSource, string fragmentSource)
    {
        _renderer = renderer;
        _compiler = compiler;
        _vsBytecode = CompileStage(compiler, vertexSource, "vs_5_0", "Vertex");
        _psBytecode = CompileStage(compiler, fragmentSource, "ps_5_0", "Fragment");
        BuildReflection();
        BuildRootSignature();
    }

    public override bool TryReload(PipelineBlob blob, [NotNullWhen(false)] out string? error)
    {
        if (blob.Backend != GraphicsBackend.D3D12) { error = $"Expected D3D12 blob, got {blob.Backend}"; return false; }
        if (blob.Format != ShaderDataFormat.SourceText) { error = $"D3D12 requires SourceText, got {blob.Format}"; return false; }

        string vsSrc = Encoding.UTF8.GetString(blob.VertexData
            ?? throw new InvalidOperationException("Reload blob has no vertex stage"));
        string psSrc = Encoding.UTF8.GetString(blob.FragmentData
            ?? throw new InvalidOperationException("Reload blob has no fragment stage"));

        byte[] vs, ps;
        try
        {
            vs = CompileStage(_compiler, vsSrc, "vs_5_0", "Vertex");
            ps = CompileStage(_compiler, psSrc, "ps_5_0", "Fragment");
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }

        // Submitted frames keep the previous program alive through retirement.
        DisposePsos();
        _renderer.Retire(ref _rootSignature);

        _vsBytecode = vs;
        _psBytecode = ps;
        BuildReflection();
        BuildRootSignature();

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
                pSrc, (nuint)sourceBytes.Length, pName, null,
                ref System.Runtime.CompilerServices.Unsafe.NullRef<ID3DInclude>(),
                pEntry, pProfile, 0u, 0u, ref code, ref errors);

            if (hr < 0)
            {
                string msg = ReadBlobString(errors);
                errors.Dispose();
                code.Dispose();
                throw new InvalidOperationException($"{stageLabel} HLSL compile failed ({hr:X}): {msg}");
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

    private void BuildReflection()
    {
        _uniforms = new Dictionary<string, UniformLocation>(StringComparer.Ordinal);
        _textureSlots = new Dictionary<string, uint>(StringComparer.Ordinal);
        var cbuffers = new List<CBufferSlot>();
        _srvCount = 0;

        ReflectStage(_vsBytecode, cbuffers);
        ReflectStage(_psBytecode, cbuffers);

        _cbuffers = cbuffers.ToArray();
    }

    private void ReflectStage(byte[] bytecode, List<CBufferSlot> cbuffers)
    {
        ID3D12ShaderReflection* refl = null;
        Guid iid = ID3D12ShaderReflection.Guid;
        fixed (byte* pBytecode = bytecode)
        {
            SilkMarshal.ThrowHResult(_compiler.Reflect(pBytecode, (nuint)bytecode.Length, &iid, (void**)&refl));
        }

        ShaderDesc shaderDesc = default;
        SilkMarshal.ThrowHResult(refl->GetDesc(&shaderDesc));

        for (uint i = 0; i < shaderDesc.ConstantBuffers; i++)
        {
            ID3D12ShaderReflectionConstantBuffer* cb = refl->GetConstantBufferByIndex(i);
            ShaderBufferDesc bufDesc = default;
            SilkMarshal.ThrowHResult(cb->GetDesc(&bufDesc));

            string name = MarshalUtf8(bufDesc.Name);
            ShaderInputBindDesc bindDesc = default;
            SilkMarshal.ThrowHResult(refl->GetResourceBindingDescByName(bufDesc.Name, &bindDesc));
            uint register = bindDesc.BindPoint;

            int existing = cbuffers.FindIndex(c => c.Register == register && c.Name == name);
            if (existing >= 0)
                continue;

            uint roundedSize = ((bufDesc.Size + 255u) / 256u) * 256u; // CBV alignment
            cbuffers.Add(new CBufferSlot(name, register, new byte[roundedSize], dirty: true));
            int newIdx = cbuffers.Count - 1;

            for (uint v = 0; v < bufDesc.Variables; v++)
            {
                ID3D12ShaderReflectionVariable* variable = cb->GetVariableByIndex(v);
                ShaderVariableDesc varDesc = default;
                SilkMarshal.ThrowHResult(variable->GetDesc(&varDesc));
                _uniforms[MarshalUtf8(varDesc.Name)] = new UniformLocation(newIdx, varDesc.StartOffset, varDesc.Size);
            }
        }

        for (uint i = 0; i < shaderDesc.BoundResources; i++)
        {
            ShaderInputBindDesc bind = default;
            SilkMarshal.ThrowHResult(refl->GetResourceBindingDesc(i, &bind));
            if (bind.Type != D3DShaderInputType.D3DSitTexture) continue;
            string name = MarshalUtf8(bind.Name);
            _textureSlots[name] = bind.BindPoint;
            _srvCount = Math.Max(_srvCount, bind.BindPoint + 1);
        }

        refl->Release();
    }

    private static string MarshalUtf8(byte* ptr)
    {
        if (ptr is null) return string.Empty;
        int len = 0;
        while (ptr[len] != 0) len++;
        return Encoding.UTF8.GetString(ptr, len);
    }

    private void BuildRootSignature()
    {
        int paramCount = _cbuffers.Length + (_srvCount > 0 ? 2 : 0);
        var parameters = stackalloc RootParameter[Math.Max(paramCount, 1)];

        for (int i = 0; i < _cbuffers.Length; i++)
        {
            parameters[i] = new RootParameter
            {
                ParameterType = RootParameterType.TypeCbv,
                ShaderVisibility = ShaderVisibility.All,
            };
            parameters[i].Anonymous.Descriptor = new RootDescriptor
            {
                ShaderRegister = _cbuffers[i].Register,
                RegisterSpace = 0,
            };
        }

        var srvRange = new DescriptorRange
        {
            RangeType = DescriptorRangeType.Srv,
            NumDescriptors = _srvCount,
            BaseShaderRegister = 0,
            RegisterSpace = 0,
            OffsetInDescriptorsFromTableStart = 0,
        };
        var samplerRange = new DescriptorRange
        {
            RangeType = DescriptorRangeType.Sampler,
            NumDescriptors = _srvCount,
            BaseShaderRegister = 0,
            RegisterSpace = 0,
            OffsetInDescriptorsFromTableStart = 0,
        };

        if (_srvCount > 0)
        {
            _srvTableParam = _cbuffers.Length;
            parameters[_srvTableParam] = new RootParameter
            {
                ParameterType = RootParameterType.TypeDescriptorTable,
                ShaderVisibility = ShaderVisibility.Pixel,
            };
            parameters[_srvTableParam].Anonymous.DescriptorTable = new RootDescriptorTable
            {
                NumDescriptorRanges = 1,
                PDescriptorRanges = &srvRange,
            };

            _samplerTableParam = _srvTableParam + 1;
            parameters[_samplerTableParam] = new RootParameter
            {
                ParameterType = RootParameterType.TypeDescriptorTable,
                ShaderVisibility = ShaderVisibility.Pixel,
            };
            parameters[_samplerTableParam].Anonymous.DescriptorTable = new RootDescriptorTable
            {
                NumDescriptorRanges = 1,
                PDescriptorRanges = &samplerRange,
            };
        }
        else
        {
            _srvTableParam = -1;
            _samplerTableParam = -1;
        }

        var desc = new RootSignatureDesc
        {
            NumParameters = (uint)paramCount,
            PParameters = paramCount > 0 ? parameters : null,
            NumStaticSamplers = 0,
            PStaticSamplers = null,
            Flags = RootSignatureFlags.AllowInputAssemblerInputLayout,
        };

        ComPtr<ID3D10Blob> blob = default;
        ComPtr<ID3D10Blob> error = default;
        int hr = _renderer.D3D12Api.SerializeRootSignature(&desc, D3DRootSignatureVersion.Version10, ref blob, ref error);
        if (hr < 0)
        {
            string msg = ReadBlobString(error);
            blob.Dispose();
            error.Dispose();
            throw new InvalidOperationException($"Root signature serialization failed ({hr:X}): {msg}");
        }

        ID3D12RootSignature* rs = null;
        Guid rsGuid = ID3D12RootSignature.Guid;
        SilkMarshal.ThrowHResult(_renderer.DevicePtr->CreateRootSignature(
            0, blob.GetBufferPointer(), blob.GetBufferSize(), &rsGuid, (void**)&rs));
        _rootSignature = ComOwnership.Own(rs);
        blob.Dispose();
        error.Dispose();
    }

    internal ID3D12PipelineState* GetPso(
        D3D12VertexLayout layout,
        FillMode fill,
        PrimitiveTopologyType topology,
        DepthMode depth,
        BlendMode blend,
        DepthBias bias,
        in D3D12TargetState target)
    {
        var key = new D3D12PsoKey(layout, fill, topology, depth, blend, bias, in target);
        if (_psoCache.TryGetValue(key, out var cached))
            return (ID3D12PipelineState*)cached.Handle;

        var pso = CreatePso(layout, fill, topology, depth, blend, bias, in target);
        _psoCache[key] = pso;
        return (ID3D12PipelineState*)pso.Handle;
    }

    // For tests and diagnostics.
    internal int PipelineStateCount => _psoCache.Count;

    private ComPtr<ID3D12PipelineState> CreatePso(
        D3D12VertexLayout layout,
        FillMode fill,
        PrimitiveTopologyType topology,
        DepthMode depth,
        BlendMode blend,
        DepthBias bias,
        in D3D12TargetState target)
    {
        Span<InputElementDesc> elements = stackalloc InputElementDesc[layout.Elements.Length];
        ReadOnlySpan<byte> sem = "TEXCOORD\0"u8;
        fixed (byte* semName = sem)
        {
            for (int i = 0; i < layout.Elements.Length; i++)
            {
                var e = layout.Elements[i];
                elements[i] = new InputElementDesc
                {
                    SemanticName = semName,
                    SemanticIndex = e.SemanticIndex,
                    Format = e.Format,
                    InputSlot = e.InputSlot,
                    AlignedByteOffset = e.ByteOffset,
                    InputSlotClass = e.PerInstance
                        ? InputClassification.PerInstanceData
                        : InputClassification.PerVertexData,
                    InstanceDataStepRate = e.PerInstance ? 1u : 0u,
                };
            }

            var desc = new GraphicsPipelineStateDesc
            {
                PRootSignature = (ID3D12RootSignature*)_rootSignature.Handle,
                SampleMask = uint.MaxValue,
                PrimitiveTopologyType = topology,
                NumRenderTargets = target.RenderTargetCount,
                DSVFormat = target.DepthFormat,
                SampleDesc = new SampleDesc(target.SampleCount, 0),
            };
            fixed (byte* vs = _vsBytecode)
            fixed (byte* ps = _psBytecode)
            {
                desc.VS = new ShaderBytecode { PShaderBytecode = vs, BytecodeLength = (nuint)_vsBytecode.Length };
                desc.PS = new ShaderBytecode { PShaderBytecode = ps, BytecodeLength = (nuint)_psBytecode.Length };
                // Every bound attachment's format, not just the first. Zero is a
                // depth-only pass.
                for (int i = 0; i < (int)target.RenderTargetCount; i++)
                    desc.RTVFormats[i] = target.ColorAt(i);

                bool alphaBlend = blend == BlendMode.AlphaBlend;
                desc.BlendState = new BlendDesc { AlphaToCoverageEnable = 0, IndependentBlendEnable = 0 };
                desc.BlendState.RenderTarget[0] = new RenderTargetBlendDesc
                {
                    BlendEnable = alphaBlend,
                    LogicOpEnable = 0,
                    SrcBlend = alphaBlend ? Blend.SrcAlpha : Blend.One,
                    DestBlend = alphaBlend ? Blend.InvSrcAlpha : Blend.Zero,
                    BlendOp = BlendOp.Add,
                    SrcBlendAlpha = Blend.One,
                    DestBlendAlpha = alphaBlend ? Blend.InvSrcAlpha : Blend.Zero,
                    BlendOpAlpha = BlendOp.Add,
                    LogicOp = LogicOp.Noop,
                    RenderTargetWriteMask = (byte)ColorWriteEnable.All,
                };

                // Same defaults as D3D11 and GL. Wireframe turns culling off.
                desc.RasterizerState = new RasterizerDesc
                {
                    FillMode = fill,
                    CullMode = fill == FillMode.Wireframe ? CullMode.None : CullMode.Back,
                    FrontCounterClockwise = 1,
                    DepthBias = bias.Constant,
                    // Unclamped: a caster edge-on to the light shades nothing anyway.
                    DepthBiasClamp = 0f,
                    SlopeScaledDepthBias = bias.SlopeScaled,
                    DepthClipEnable = 1,
                    MultisampleEnable = 0,
                    AntialiasedLineEnable = 0,
                    ForcedSampleCount = 0,
                    ConservativeRaster = ConservativeRasterizationMode.Off,
                };

                var stencilOp = new DepthStencilopDesc
                {
                    StencilFailOp = StencilOp.Keep,
                    StencilDepthFailOp = StencilOp.Keep,
                    StencilPassOp = StencilOp.Keep,
                    StencilFunc = ComparisonFunc.Always,
                };
                desc.DepthStencilState = new DepthStencilDesc
                {
                    DepthEnable = depth != DepthMode.None,
                    DepthWriteMask = depth is DepthMode.TestWrite or DepthMode.TestWriteEqual
                        ? DepthWriteMask.All
                        : DepthWriteMask.Zero,
                    DepthFunc = depth is DepthMode.TestWriteEqual or DepthMode.TestNoWriteEqual
                        ? ComparisonFunc.LessEqual
                        : ComparisonFunc.Less,
                    StencilEnable = 0,
                    StencilReadMask = 0xFF,
                    StencilWriteMask = 0xFF,
                    FrontFace = stencilOp,
                    BackFace = stencilOp,
                };

                fixed (InputElementDesc* pElements = elements)
                {
                    desc.InputLayout = new InputLayoutDesc
                    {
                        PInputElementDescs = pElements,
                        NumElements = (uint)elements.Length,
                    };

                    ID3D12PipelineState* pso = null;
                    Guid psoGuid = ID3D12PipelineState.Guid;
                    SilkMarshal.ThrowHResult(_renderer.DevicePtr->CreateGraphicsPipelineState(&desc, &psoGuid, (void**)&pso));
                    return ComOwnership.Own(pso);
                }
            }
        }
    }

    public override void Use()
    {
        var list = _renderer.CurrentList;
        if (list is null) return;

        _renderer.BindRootSignature(list, (nint)_rootSignature.Handle);
        _renderer.CurrentProgram = this;

        // A clean cbuffer rebinds this frame's slice. The ring restarts every
        // frame, so the first use in a frame always uploads.
        ulong frame = _renderer.FrameNumber;
        for (int i = 0; i < _cbuffers.Length; i++)
        {
            ref var slot = ref _cbuffers[i];
            if (slot.Dirty || slot.LastUploadFrame != frame)
            {
                var slice = _renderer.AllocUpload((uint)slot.Shadow.Length, 256);
                slot.Shadow.CopyTo(new Span<byte>(slice.Cpu, slot.Shadow.Length));
                slot.GpuVa = slice.GpuVa;
                slot.Dirty = false;
                slot.LastUploadFrame = frame;
            }
            _renderer.BindRootCbv(list, i, slot.GpuVa);
        }

        if (_srvTableParam >= 0 && _srvCount > 0)
        {
            var (srvTable, samplerTable) = _renderer.StageDescriptors(_pendingTextures, _srvCount);
            // Otherwise a draw that binds fewer textures inherits the last draw's.
            _pendingTextures.Clear();
            list->SetGraphicsRootDescriptorTable((uint)_srvTableParam, srvTable);
            list->SetGraphicsRootDescriptorTable((uint)_samplerTableParam, samplerTable);
        }
    }

    public override void SetUniform(string name, Matrix4x4 value) => Write(name, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
    public override void SetUniform(string name, Vector4 value) => Write(name, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
    public override void SetUniform(string name, Vector3 value)
    {
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

    private void WriteArray(string name, ReadOnlySpan<byte> bytes, int count, int stride, string elementType)
    {
        // A shader may not declare every uniform a pipeline sets.
        if (!_uniforms.TryGetValue(name, out var loc)) return;
        if (count == 0) return;

        // Throw, don't clamp: a short write leaves the last upload's tail in
        // place, e.g. lights that are no longer there.
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

    private void WriteBytes(UniformLocation loc, ReadOnlySpan<byte> bytes)
    {
        ref var slot = ref _cbuffers[loc.CBufferIndex];
        var target = slot.Shadow.AsSpan((int)loc.Offset, bytes.Length);

        // An unchanged write keeps the slot clean, so Use() uploads nothing.
        if (bytes.SequenceEqual(target)) return;
        bytes.CopyTo(target);
        slot.Dirty = true;
    }

    public override void SetTexture(string name, int unit, Texture texture)
    {
        if (texture is not D3D12Texture d3dTex)
            throw new ArgumentException($"Texture must be D3D12Texture, got {texture.GetType().Name}", nameof(texture));
        if (!_textureSlots.TryGetValue(name, out uint slot)) return;
        _pendingTextures[slot] = d3dTex;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposePsos();
        _renderer.Retire(ref _rootSignature);
    }

    private void DisposePsos()
    {
        foreach (var pso in _psoCache.Values)
        {
            var owned = pso;
            _renderer.Retire(ref owned);
        }
        _psoCache.Clear();
    }

    private record struct UniformLocation(int CBufferIndex, uint Offset, uint Size);

    private struct CBufferSlot
    {
        public string Name;
        public uint Register;
        public byte[] Shadow;
        public bool Dirty;

        // Valid to rebind only while LastUploadFrame is the current frame.
        public ulong GpuVa;
        public ulong LastUploadFrame;

        public CBufferSlot(string name, uint register, byte[] shadow, bool dirty)
        {
            Name = name;
            Register = register;
            Shadow = shadow;
            Dirty = dirty;
            GpuVa = 0;
            LastUploadFrame = 0;
        }
    }
}

internal sealed class D3D12VertexLayout
{
    // SemanticIndex is the TEXCOORD index. ByteOffset is within the element's
    // own slot. InputSlot 0 is the mesh, 1 the instance buffer.
    public readonly record struct Element(
        uint SemanticIndex,
        Format Format,
        uint ByteOffset,
        uint InputSlot = 0,
        bool PerInstance = false);

    public Element[] Elements { get; }
    public uint StrideBytes { get; }

    // Hash for bucketing only. PSO identity compares the elements.
    public int Key { get; }

    public D3D12VertexLayout(Element[] elements, uint strideBytes)
    {
        Elements = elements;
        StrideBytes = strideBytes;

        var hash = new HashCode();
        hash.Add(strideBytes);
        foreach (var e in elements)
        {
            hash.Add(e.SemanticIndex);
            hash.Add((int)e.Format);
            hash.Add(e.ByteOffset);
            hash.Add(e.InputSlot);
            hash.Add(e.PerInstance);
        }
        Key = hash.ToHashCode();
    }
}
