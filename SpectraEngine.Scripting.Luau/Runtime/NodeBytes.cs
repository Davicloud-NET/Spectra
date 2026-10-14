namespace SpectraEngine.Scripting.Luau.Runtime;

// What scripts keep on the nodes that are not destroyed, in tags and
// attributes, by the estimate of NodeWeight. The tables add to it as they
// change, and the host counts it against the memory budget. It is no garbage
// while the node lives, so Luau's collector is not told of it.
internal sealed class NodeBytes(LuauHost? host)
{
    public long Bytes { get; private set; }

    // The most there was since the level started.
    public long Peak { get; private set; }

    public void Add(long bytes)
    {
        if (bytes == 0)
            return;

        Bytes += bytes;
        Peak = Math.Max(Peak, Bytes);
        if (host is not null)
            host.NodeBytes = Bytes;
    }
}
