namespace SpectraEngine.Scripting.Luau;

/// <summary>What kind of value a stack slot holds.</summary>
// The numbers are Luau's own (LuaType), which the startup check compares with the library.
public enum LuauType
{
    /// <summary>Nothing at that index.</summary>
    None = -1,

    /// <summary>Nil.</summary>
    Nil = 0,

    /// <summary>True or false.</summary>
    Boolean = 1,

    /// <summary>A bare pointer. The binding makes none.</summary>
    LightUserdata = 2,

    /// <summary>A number, which in Luau is a double.</summary>
    Number = 3,

    /// <summary>Luau's 64-bit integer type.</summary>
    Integer = 4,

    /// <summary>Three floats, Luau's own vector type.</summary>
    Vector = 5,

    /// <summary>A string of bytes, UTF-8 by convention.</summary>
    String = 6,

    /// <summary>A table.</summary>
    Table = 7,

    /// <summary>A function, of a script or of the host.</summary>
    Function = 8,

    /// <summary>A userdata: a managed object's handle, or a value of the host's own.</summary>
    Userdata = 9,

    /// <summary>A coroutine.</summary>
    Thread = 10,

    /// <summary>A block of bytes a script made.</summary>
    Buffer = 11,
}
