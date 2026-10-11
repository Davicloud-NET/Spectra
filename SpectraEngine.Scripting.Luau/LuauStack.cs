using System.Numerics;
using System.Runtime.CompilerServices;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

/// <summary>
/// The values of one Luau thread, counted from 1 at the bottom or from -1 at
/// the top. A host function reads its arguments here and pushes its results.
/// A method that pushes answers false when it could not: an error value is
/// then on top instead. No method here allocates, but <see cref="GetText"/>.
/// </summary>
public readonly unsafe ref struct LuauStack
{
    /// <summary>The index that stands for the globals of the script that is running.</summary>
    public const int Globals = Lua.GlobalsIndex;

    /// <summary>What a host function returns to raise the value on top as it is.</summary>
    public const int Raise = Shim.HostRaise;

    private readonly nint _state;

    /// <summary>Wraps the state a host function was called with.</summary>
    public LuauStack(nint state)
    {
        ArgumentOutOfRangeException.ThrowIfZero(state);
        _state = state;
    }

    /// <summary>The Luau state, for a function pointer that takes one.</summary>
    public nint State => _state;

    /// <summary>How many values there are. In a host function, how many arguments it got.</summary>
    public int Count => Lua.lua_gettop(_state);

    /// <summary>What kind of value sits at the index.</summary>
    public LuauType TypeOf(int index) => (LuauType)Lua.lua_type(_state, At(index));

    /// <summary>Whether there is nil at the index, or nothing.</summary>
    public bool IsNil(int index) => Lua.lua_type(_state, At(index)) <= LuaType.Nil;

    /// <summary>Reads a number. A string that reads as a number counts, as it does in Luau.</summary>
    public bool TryGetNumber(int index, out double value)
    {
        int isNumber;
        value = Lua.lua_tonumberx(_state, At(index), &isNumber);
        return isNumber != 0;
    }

    /// <summary>Whether the value counts as true in Luau: anything but false and nil.</summary>
    public bool GetBoolean(int index) => Lua.lua_toboolean(_state, At(index)) != 0;

    /// <summary>Reads a vector.</summary>
    public bool TryGetVector(int index, out Vector3 value)
    {
        float* parts = Lua.lua_tovector(_state, At(index));
        value = parts == null ? default : new Vector3(parts[0], parts[1], parts[2]);
        return parts != null;
    }

    /// <summary>Reads a string's bytes where Luau keeps them. They live while the string stays on the stack.</summary>
    public bool TryGetUtf8(int index, out ReadOnlySpan<byte> text) => LuaText.TryReadUtf8(_state, At(index), out text);

    /// <summary>Reads a string, or null when the value is not one. Allocates.</summary>
    public string? GetText(int index) => LuaText.Read(_state, At(index));

    /// <summary>Reads the block of a userdata made by <see cref="PushUserdata"/> with this tag.</summary>
    public bool TryGetUserdata<T>(int index, int tag, out T value)
        where T : unmanaged
    {
        index = At(index);
        void* block = Lua.lua_touserdatatagged(_state, index, tag);
        bool isFound = block != null && Lua.lua_objlen(_state, index) >= sizeof(T);
        value = isFound ? Unsafe.ReadUnaligned<T>(block) : default;
        return isFound;
    }

    /// <summary>Pushes nil.</summary>
    public bool PushNil() => Shim.sl_pushnil(_state) == LuaStatus.Ok;

    /// <summary>Pushes a boolean.</summary>
    public bool Push(bool value) => Shim.sl_pushboolean(_state, value ? 1 : 0) == LuaStatus.Ok;

    /// <summary>Pushes a number.</summary>
    public bool Push(double value) => Shim.sl_pushnumber(_state, value) == LuaStatus.Ok;

    /// <summary>Pushes a vector.</summary>
    public bool Push(Vector3 value) => Shim.sl_pushvector(_state, value.X, value.Y, value.Z) == LuaStatus.Ok;

    /// <summary>Pushes a string.</summary>
    public bool Push(ReadOnlySpan<char> text) => LuaText.Push(_state, text) == LuaStatus.Ok;

    /// <summary>Pushes a string from its UTF-8 bytes.</summary>
    public bool PushUtf8(ReadOnlySpan<byte> text) => LuaText.PushUtf8(_state, text) == LuaStatus.Ok;

    /// <summary>Pushes a second copy of the value at the index.</summary>
    public bool PushCopy(int index) => Shim.sl_pushvalue(_state, index) == LuaStatus.Ok;

    /// <summary>
    /// Pushes a function written as a static <c>[UnmanagedCallersOnly]</c> method, for the calls
    /// that must be fast. The method must let no exception out: it ends in a catch that returns
    /// <see cref="Fail(Exception)"/>.
    /// </summary>
    public bool PushFunction(delegate* unmanaged[Cdecl]<nint, int> function, ReadOnlySpan<byte> name) =>
        HostFunctions.Push(_state, function, name);

    /// <summary>
    /// Pushes a userdata that holds a copy of the value. It gets the metatable of its tag when
    /// <see cref="SetUserdataMetatable"/> gave the tag one.
    /// </summary>
    public bool PushUserdata<T>(int tag, in T value)
        where T : unmanaged
    {
        void* block;
        if (Shim.sl_newuserdata(_state, (nuint)sizeof(T), tag, &block) != LuaStatus.Ok)
            return false;

        Unsafe.WriteUnaligned(block, value);
        return true;
    }

    /// <summary>Pops so many values, or all there are.</summary>
    public void Pop(int count = 1) => Shim.sl_pop(_state, count);

    /// <summary>Pushes a new table.</summary>
    public bool NewTable(int arraySize = 0, int recordSize = 0) =>
        Shim.sl_createtable(_state, arraySize, recordSize) == LuaStatus.Ok;

    /// <summary>Pushes the field of the value at the index. This can run a script's <c>__index</c>.</summary>
    public bool GetField(int index, ReadOnlySpan<byte> name)
    {
        fixed (byte* key = name)
            return Shim.sl_getfield(_state, index, key, (nuint)name.Length) == LuaStatus.Ok;
    }

    /// <summary>Sets a field to the value on top and takes that value. A frozen table refuses.</summary>
    public bool SetField(int index, ReadOnlySpan<byte> name)
    {
        fixed (byte* key = name)
            return Shim.sl_setfield(_state, index, key, (nuint)name.Length) == LuaStatus.Ok;
    }

    /// <summary>Pushes a global as the running script sees it.</summary>
    public bool GetGlobal(ReadOnlySpan<byte> name) => GetField(Globals, name);

    /// <summary>Sets a global to the value on top and takes that value. Once the host is sealed, only a script's own globals can be set.</summary>
    public bool SetGlobal(ReadOnlySpan<byte> name) => SetField(Globals, name);

    /// <summary>Pushes the nth element of the table at the index, with no metamethod.</summary>
    public bool GetElement(int index, int n) => Shim.sl_rawgeti(_state, index, n) == LuaStatus.Ok;

    /// <summary>Sets the nth element to the value on top and takes that value, with no metamethod.</summary>
    public bool SetElement(int index, int n) => Shim.sl_rawseti(_state, index, n) == LuaStatus.Ok;

    /// <summary>The length of a table's array part, of a string or of a userdata block.</summary>
    public int Length(int index) => Lua.lua_objlen(_state, At(index));

    /// <summary>
    /// One step of a walk over the table at the index. Start with a cursor of 0. A step
    /// that finds a pair pushes its key and its value: pop both before the next step.
    /// </summary>
    public LuauWalk Next(int index, ref int cursor)
    {
        int next;
        if (Shim.sl_rawiter(_state, index, cursor, &next) != LuaStatus.Ok)
            return LuauWalk.Error;

        cursor = next;
        return next < 0 ? LuauWalk.End : LuauWalk.Pair;
    }

    /// <summary>Makes the table at the index read-only, for scripts and for the host.</summary>
    public bool Freeze(int index) => Shim.sl_freeze(_state, index) == LuaStatus.Ok;

    /// <summary>
    /// Takes the table on top and makes it the metatable of every userdata with this tag.
    /// The table is frozen, since all scripts share it. A tag takes one metatable, once.
    /// </summary>
    public bool SetUserdataMetatable(int tag) => Shim.sl_setuserdatametatable(_state, tag, 0) == LuaStatus.Ok;

    /// <summary>Pushes the value at the index as text, the way <c>tostring</c> gives it.</summary>
    public bool PushAsText(int index) => Shim.sl_tostring(_state, index) == LuaStatus.Ok;

    /// <summary>
    /// Calls the function under the top <paramref name="arguments"/> values and leaves its
    /// results. The function cannot yield. False: the error value is there instead.
    /// </summary>
    public bool Call(int arguments, int results) => Shim.sl_call(_state, arguments, results) == LuaStatus.Ok;

    /// <summary>For a host function to return: the script gets an error with this message and its own position.</summary>
    public int Fail(ReadOnlySpan<char> message) => LuaText.Fail(_state, message);

    /// <summary>For the catch at the end of a host function: the exception becomes a script error.</summary>
    public int Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return LuaText.Fail(_state, exception.Message);
    }

    /// <summary>
    /// For a host function to return: the script waits, and the top <paramref name="count"/>
    /// values go to whoever resumed it. What it is resumed with become the results of this call.
    /// </summary>
    public static int Yield(int count = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return Shim.HostYield - count;
    }

    // Luau checks an index in a debug build only, and a negative one past the
    // bottom reads below the frame. A positive one past the top reads as none.
    private int At(int index)
    {
        if (index > 0 || index == Globals)
            return index;

        int top = Lua.lua_gettop(_state);
        if (index == 0 || -index > top)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"The stack holds {top} values.");
        return top + index + 1;
    }
}
