using System.Diagnostics.CodeAnalysis;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

/// <summary>
/// The managed objects scripts hold. A script gets a userdata that names a slot here
/// and its generation, so what it still holds of a released object reads as released.
/// </summary>
public sealed unsafe class LuauObjects
{
    private const int DrainBatch = 64;

    private readonly LuauHost _host;
    private readonly Dictionary<object, int> _slotOf = new(ReferenceEqualityComparer.Instance);
    private Slot[] _slots = new Slot[64];
    private int _used;
    private int _firstFree = -1;

    internal LuauObjects(LuauHost host) => _host = host;

    /// <summary>How many objects scripts can reach. One that only grows is a leak.</summary>
    public int Count => _slotOf.Count;

    /// <summary>
    /// Takes the table on top of the stack and makes it the metatable of every object
    /// pushed with this tag. The table is frozen with all it holds, since all scripts share it. Do this
    /// before the host is sealed. A tag takes one metatable, once.
    /// </summary>
    public bool SetMetatable(LuauStack stack, int tag) =>
        !_host.IsClosed && Shim.sl_setuserdatametatable(stack.State, tag, 1) == LuaStatus.Ok;

    /// <summary>
    /// Pushes the userdata that stands for the object. While a script still holds the
    /// one from an earlier push, that same one is pushed, so the two are equal in Luau.
    /// False: it could not be pushed, and an error value is on top instead.
    /// </summary>
    public bool Push(LuauStack stack, int tag, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_host.IsClosed)
            return false;

        bool isNew = !_slotOf.TryGetValue(value, out int index);
        if (isNew)
            index = Add(value);

        uint generation = _slots[index].Generation;
        int created;
        LuaStatus status = Shim.sl_pushobject(stack.State, tag, (uint)index, generation, generation + 1, &created);

        // A userdata made now replaces one the collector has cleared away or
        // is about to. Its report will name the old generation.
        if (created != 0)
            _slots[index].Generation = generation + 1;
        else if (isNew)
            Free(index);

        return status == LuaStatus.Ok;
    }

    /// <summary>Reads the object behind the userdata at the index.</summary>
    public LuauObjectLookup TryGet(LuauStack stack, int index, int tag, [NotNullWhen(true)] out object? value)
    {
        value = null;
        if (_host.IsClosed || stack.TypeOf(index) != LuauType.Userdata)
            return LuauObjectLookup.NotThisKind;

        // TypeOf has checked the index.
        uint* handle = (uint*)Lua.lua_touserdatatagged(stack.State, index, tag);
        if (handle == null)
            return LuauObjectLookup.NotThisKind;

        uint slot = handle[0];
        if (slot >= (uint)_used || _slots[slot].Generation != handle[1] || _slots[slot].Value is not { } found)
            return LuauObjectLookup.Released;

        value = found;
        return LuauObjectLookup.Found;
    }

    /// <summary>
    /// Lets an object go, for one that left the level. A userdata a script still holds
    /// of it reads as released from now on. False when no script could reach it.
    /// </summary>
    public bool Release(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_slotOf.TryGetValue(value, out int index))
            return false;

        Free(index);
        return true;
    }

    /// <summary>
    /// Lets go of every object whose userdata Luau has collected. The table also does
    /// this by itself before it grows. Call it once a tick to keep <see cref="Count"/> current.
    /// </summary>
    public void ReleaseCollected()
    {
        if (_host.IsClosed)
            return;

        ulong* batch = stackalloc ulong[DrainBatch];
        int count;
        while ((count = Shim.sl_takereleased(_host.Stack.State, batch, DrainBatch)) > 0)
        {
            for (int i = 0; i < count; i++)
            {
                // The report of a userdata that was since replaced names an older generation.
                uint slot = (uint)(batch[i] >> 32);
                if (slot < (uint)_used && _slots[slot].Value is not null && _slots[slot].Generation == (uint)batch[i])
                    Free((int)slot);
            }
        }
    }

    private int Add(object value)
    {
        if (_firstFree < 0 && _used == _slots.Length)
            ReleaseCollected();

        int index = _firstFree;
        if (index >= 0)
            _firstFree = _slots[index].NextFree;
        else
        {
            if (_used == _slots.Length)
                Array.Resize(ref _slots, _used * 2);
            index = _used++;
        }

        _slots[index].Value = value;
        _slotOf.Add(value, index);
        return index;
    }

    // The generation moves on, so nothing made for the old one matches again.
    private void Free(int index)
    {
        ref Slot slot = ref _slots[index];
        if (slot.Value is { } value)
            _slotOf.Remove(value);

        slot.Value = null;
        slot.Generation++;
        slot.NextFree = _firstFree;
        _firstFree = index;
    }

    private struct Slot
    {
        internal object? Value;
        internal uint Generation;
        internal int NextFree;
    }
}
