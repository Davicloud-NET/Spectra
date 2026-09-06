using Silk.NET.OpenGL;
using SpectraEngine.Core.Diagnostics;

namespace SpectraEngine.Core.Graphics.OpenGL;

internal sealed class OpenGLGpuTimer : GpuTimestampTimer
{
    private readonly GL _gl;
    private readonly uint[] _queries = new uint[Slots * Marks];
    internal OpenGLGpuTimer(GL gl)
    {
        _gl = gl;
        for (int i = 0; i < _queries.Length; i++) _queries[i] = gl.GenQuery();
    }
    protected override void WriteTimestamp(int slot, int mark) =>
        _gl.QueryCounter(_queries[slot * Marks + mark], QueryCounterTarget.Timestamp);
    protected override void EndQueries(int slot, int count) { }
    protected override bool TryRead(int slot, int count, ulong[] values, out ulong frequency)
    {
        frequency = 1_000_000_000;
        _gl.GetQueryObject(_queries[slot * Marks + 1], QueryObjectParameterName.ResultAvailable, out int ready);
        if (ready == 0) return false;
        for (int i = 0; i < count; i++)
            _gl.GetQueryObject(_queries[slot * Marks + i], QueryObjectParameterName.Result, out values[i]);
        return true;
    }
    public override void Dispose()
    {
        for (int i = 0; i < _queries.Length; i++)
        {
            if (_queries[i] != 0) _gl.DeleteQuery(_queries[i]);
            _queries[i] = 0;
        }
    }
}
