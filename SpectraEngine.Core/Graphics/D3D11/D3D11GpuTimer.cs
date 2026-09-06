using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using SpectraEngine.Core.Diagnostics;

namespace SpectraEngine.Core.Graphics.D3D11;

internal unsafe sealed class D3D11GpuTimer : GpuTimestampTimer
{
    private readonly ID3D11DeviceContext* _context;
    private readonly ComPtr<ID3D11Query>[] _queries = new ComPtr<ID3D11Query>[Slots * Marks];
    private readonly ComPtr<ID3D11Query>[] _disjoint = new ComPtr<ID3D11Query>[Slots];
    internal D3D11GpuTimer(ID3D11Device* device, ID3D11DeviceContext* context)
    {
        _context = context;
        try
        {
            for (int i = 0; i < _queries.Length; i++) _queries[i] = Create(device, Query.Timestamp);
            for (int i = 0; i < Slots; i++) _disjoint[i] = Create(device, Query.TimestampDisjoint);
        }
        catch { Dispose(); throw; }
    }
    private static ComPtr<ID3D11Query> Create(ID3D11Device* device, Query kind)
    {
        var desc = new QueryDesc { Query = kind };
        ID3D11Query* query = null;
        SilkMarshal.ThrowHResult(device->CreateQuery(&desc, &query));
        return ComOwnership.Own(query);
    }
    protected override void BeginQueries(int slot) => _context->Begin((ID3D11Asynchronous*)_disjoint[slot].Handle);
    protected override void WriteTimestamp(int slot, int mark) => _context->End((ID3D11Asynchronous*)_queries[slot * Marks + mark].Handle);
    protected override void EndQueries(int slot, int count) => _context->End((ID3D11Asynchronous*)_disjoint[slot].Handle);
    protected override bool TryRead(int slot, int count, ulong[] values, out ulong frequency)
    {
        frequency = 0;
        QueryDataTimestampDisjoint data;
        int result = _context->GetData((ID3D11Asynchronous*)_disjoint[slot].Handle, &data, (uint)sizeof(QueryDataTimestampDisjoint), 1);
        if (result != 0) return result < 0; // error: completed but invalid; S_FALSE: still pending
        if (data.Disjoint) return true;
        for (int i = 0; i < count; i++)
        {
            ulong value;
            result = _context->GetData((ID3D11Asynchronous*)_queries[slot * Marks + i].Handle, &value, sizeof(ulong), 1);
            if (result != 0) return result < 0;
            values[i] = value;
        }
        frequency = data.Frequency;
        return true;
    }
    public override void Dispose()
    {
        for (int i = 0; i < _queries.Length; i++) ComOwnership.Release(ref _queries[i]);
        for (int i = 0; i < Slots; i++) ComOwnership.Release(ref _disjoint[i]);
    }
}
