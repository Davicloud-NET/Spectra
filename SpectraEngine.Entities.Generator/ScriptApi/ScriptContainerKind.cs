namespace SpectraEngine.Entities.Generator.ScriptApi;

// What a class with a script attribute declares.
internal enum ScriptContainerKind : byte
{
    // [ScriptClass]: objects a script holds by reference.
    Class,

    // [ScriptValue]: a type a script holds by value.
    Value,

    // [ScriptLibrary]: a global table of functions and constants.
    Library,

    // [ScriptGlobals]: global values and functions.
    Globals,
}
