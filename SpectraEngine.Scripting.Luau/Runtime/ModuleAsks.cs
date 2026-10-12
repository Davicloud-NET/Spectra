namespace SpectraEngine.Scripting.Luau.Runtime;

// The paths that found a module, by the file that asked and the string it
// handed over, both as the bytes Luau holds. A script that asks again, as a
// handler that asks on every tick does, then makes no string and walks no path.
internal sealed class ModuleAsks
{
    private readonly Dictionary<int, Ask> _byHash = [];

    public ScriptModule? Find(ReadOnlySpan<byte> asker, ReadOnlySpan<byte> asked)
    {
        for (Ask? ask = _byHash.GetValueOrDefault(HashOf(asker, asked)); ask is not null; ask = ask.Next)
        {
            if (asker.SequenceEqual(ask.Asker) && asked.SequenceEqual(ask.Asked))
                return ask.Module;
        }

        return null;
    }

    public void Add(ReadOnlySpan<byte> asker, ReadOnlySpan<byte> asked, ScriptModule module)
    {
        int hash = HashOf(asker, asked);
        _byHash[hash] = new Ask(asker.ToArray(), asked.ToArray(), module, _byHash.GetValueOrDefault(hash));
    }

    // For when a module is forgotten: an ask that found it has to look again.
    public void Clear() => _byHash.Clear();

    // The two are hashed apart, so "a" asking for "bc" is not "ab" asking for "c".
    private static int HashOf(ReadOnlySpan<byte> asker, ReadOnlySpan<byte> asked)
    {
        HashCode hash = default;
        hash.AddBytes(asker);
        hash.Add(asker.Length);
        hash.AddBytes(asked);
        return hash.ToHashCode();
    }

    // Two asks with one hash are a chain.
    private sealed record Ask(byte[] Asker, byte[] Asked, ScriptModule Module, Ask? Next);
}
