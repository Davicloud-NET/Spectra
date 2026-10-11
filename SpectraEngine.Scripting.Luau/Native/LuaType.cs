namespace SpectraEngine.Scripting.Luau.Native;

// lua_Type in lua.h. The numbers move between Luau releases and with the
// vector build options, so LuauLibrary compares them with the library.
internal enum LuaType
{
    None = -1,
    Nil = 0,
    Boolean = 1,
    LightUserdata = 2,
    Number = 3,
    Integer = 4,
    Vector = 5,
    String = 6,
    Table = 7,
    Function = 8,
    Userdata = 9,
    Thread = 10,
    Buffer = 11,

    // No script can make these two: their feature flags are off.
    Class = 12,
    Object = 13,
}
