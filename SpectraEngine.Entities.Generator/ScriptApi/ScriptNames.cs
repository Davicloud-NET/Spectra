using Microsoft.CodeAnalysis.CSharp;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// The types of the binding and of Core that generated code names. Every
// one is global::-qualified, as in the entity emitter: the file lands in
// the author's namespace.
internal static class ScriptNames
{
    // Where the catalogue goes, and where the binding keeps what glue calls.
    public const string RuntimeNamespace = "SpectraEngine.Scripting.Luau.Runtime";

    public const string Runtime = "global::" + RuntimeNamespace;
    public const string Api = "global::SpectraEngine.Core.Scripting.Api";
    public const string Core = "global::SpectraEngine.Core.Scripting";

    public const string Stack = "global::SpectraEngine.Scripting.Luau.LuauStack";
    public const string Context = Runtime + ".ScriptContext";
    public const string Arguments = Runtime + ".ScriptArguments";
    public const string Member = Runtime + ".ScriptMember";
    public const string Atoms = Runtime + ".ScriptAtoms";
    public const string ClassIds = Runtime + ".ScriptClassIds";
    public const string ApiTable = Runtime + ".ApiTable";
    public const string MethodTable = Runtime + ".MethodTable";
    public const string FrameValue = Runtime + ".FrameValue";

    public const string Raise = Stack + ".Raise";

    public static string Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    public static string Utf8(string text) => Literal(text) + "u8";

    // A Luau name as a C# identifier. A script may call a member "new".
    public static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
