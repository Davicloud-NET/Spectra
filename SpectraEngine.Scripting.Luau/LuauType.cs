namespace SpectraEngine.Scripting.Luau;

/// <summary>What kind of value a stack slot holds.</summary>
// The numbers are Luau's own (LuaType), which the startup check compares with the library.
public enum LuauType
{
    /// <summary>Nothing at that index.</summary>
    None = -1,
    Nil = 0,
    Boolean = 1,
    LightUserdata = 2,
    Number = 3,
    Integer = 4,

    /// <summary>Three floats, Luau's own vector type.</summary>
    Vector = 5,
    String = 6,
    Table = 7,
    Function = 8,
    Userdata = 9,
    Thread = 10,
    Buffer = 11,
}
