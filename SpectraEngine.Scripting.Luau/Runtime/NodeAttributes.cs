using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What scripts left on one node: a value for each name they set, and the
// signals they asked for, each made on the first ask. A name with no value
// is one a script took away from an entity's keyvalues, which a script
// reads as nil from then on.
internal sealed class NodeAttributes
{
    // Past this many names a dictionary finds one sooner than a walk does.
    public const int MostWalked = 32;

    // Slots keep a dictionary's order, the slot let go last taken first. Where
    // names collide in the table GetAttributes fills, that order decides pairs.
    private Slot _first;
    private object? _store;
    private ushort _used;
    private ushort _freeCount;
    private short _free = -1;

    // AttributeChanged.
    public ScriptSignal? Changed { get; set; }

    // GetAttributeChangedSignal, by the attribute's name.
    public Dictionary<string, ScriptSignal>? ChangedOf { get; set; }

    public int Count => _store is Dictionary<string, MemberValue> many ? many.Count : _used - _freeCount;

    public bool TryGet(ReadOnlySpan<char> name, out MemberValue value)
    {
        if (_store is Dictionary<string, MemberValue> many)
            return many.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out value);

        int index = IndexOf(name);
        value = index >= 0 ? At(index).Value : MemberValue.None;
        return index >= 0;
    }

    public bool Contains(string name) => TryGet(name, out _);

    public void Set(string name, in MemberValue value)
    {
        if (_store is Dictionary<string, MemberValue> many)
        {
            many[name] = value;
            return;
        }

        int index = IndexOf(name);
        if (index < 0 && _freeCount > 0)
        {
            index = _free;
            _free = (short)At(index).Value.Number;
            _freeCount--;
        }
        else if (index < 0 && _used == MostWalked)
        {
            GoOver(name, in value);
            return;
        }
        else if (index < 0)
        {
            index = _used;
            if (index > 0)
                Grow();

            _used++;
        }

        At(index) = new Slot(name, value);
    }

    public bool Remove(ReadOnlySpan<char> name)
    {
        if (_store is Dictionary<string, MemberValue> many)
            return many.GetAlternateLookup<ReadOnlySpan<char>>().Remove(name);

        int index = IndexOf(name);
        if (index < 0)
            return false;

        // A slot let go holds no name, and the slot let go before it.
        At(index) = new Slot(null, MemberValue.Of(_free));
        _free = (short)index;
        _freeCount++;
        return true;
    }

    public Enumerator GetEnumerator() => new(this);

    // Ends every listener of the node's signals and lets the signals go, for
    // a node that fires nothing more. The values stay.
    public void EndSignals()
    {
        // Out first: ending a thread can run a script that asks for a signal.
        ScriptSignal? changed = Changed;
        Dictionary<string, ScriptSignal>? byName = ChangedOf;
        Changed = null;
        ChangedOf = null;

        changed?.EndAll();
        if (byName is null)
            return;

        foreach (ScriptSignal signal in byName.Values)
            signal.EndAll();
    }

    private ref Slot At(int index) => ref index == 0 ? ref _first : ref ((Slot[]?)_store ?? [])[index - 1];

    private int IndexOf(ReadOnlySpan<char> name)
    {
        for (int i = 0; i < _used; i++)
        {
            if (At(i).Name is { } held && name.SequenceEqual(held))
                return i;
        }

        return -1;
    }

    // One more slot. The array grows by one, since a node keeps what it has
    // for a long time and gets a new name seldom.
    private void Grow()
    {
        var rest = (Slot[]?)_store ?? [];
        var grown = new Slot[rest.Length + 1];
        rest.CopyTo(grown, 0);
        _store = grown;
    }

    // Only full slots go over, so the dictionary has no hole and keeps
    // their order.
    private void GoOver(string name, in MemberValue value)
    {
        var many = new Dictionary<string, MemberValue>(MostWalked * 2, StringComparer.Ordinal);
        for (int i = 0; i < _used; i++)
        {
            Slot slot = At(i);
            many.Add(slot.Name ?? string.Empty, slot.Value);
        }

        many.Add(name, value);
        _first = default;
        _store = many;
    }

    private readonly record struct Slot(string? Name, MemberValue Value);

    // The names and values in the order a dictionary hands them out.
    public struct Enumerator
    {
        private readonly NodeAttributes _owner;
        private Dictionary<string, MemberValue>.Enumerator _many;
        private readonly bool _isMany;
        private int _index;

        public Enumerator(NodeAttributes owner)
        {
            _owner = owner;
            _index = -1;
            _isMany = owner._store is Dictionary<string, MemberValue>;
            if (owner._store is Dictionary<string, MemberValue> many)
                _many = many.GetEnumerator();
        }

        public KeyValuePair<string, MemberValue> Current { get; private set; }

        public bool MoveNext()
        {
            if (_isMany)
            {
                if (!_many.MoveNext())
                    return false;

                Current = _many.Current;
                return true;
            }

            while (++_index < _owner._used)
            {
                Slot slot = _owner.At(_index);
                if (slot.Name is { } name)
                {
                    Current = new KeyValuePair<string, MemberValue>(name, slot.Value);
                    return true;
                }
            }

            return false;
        }
    }
}
