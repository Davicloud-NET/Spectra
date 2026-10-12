using Microsoft.CodeAnalysis;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// All errors: each is a declaration the generator cannot turn into a
// binding. Ids are frozen. New ones take the next free number.
internal static class ScriptApiDiagnostics
{
    private const string Category = "SpectraScriptApi";

    public static readonly DiagnosticDescriptor NotPartial = new(
        id: "SPS001",
        title: "Script API class must be partial",
        messageFormat:
            "'{0}' declares a part of the script API and must be partial, with every type that contains it. The " +
            "generator writes the other half of it.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoSummary = new(
        id: "SPS002",
        title: "Script API member has no summary",
        messageFormat: "'{0}' has no /// summary. The definitions file and the reference take their words from it.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypeNotCarried = new(
        id: "SPS003",
        title: "Type no script value is made from",
        messageFormat:
            "'{0}' uses the type '{1}', which no script value is made from. Use bool, int, float, double, string, " +
            "ReadOnlySpan<byte>, Vector3, CFrame, SceneNode or a class with [ScriptClass]. Or write it by hand: " +
            "take a LuauStack, return an int, and say the parameters in the attribute.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotLuau = new(
        id: "SPS004",
        title: "Text does not read as Luau writes it",
        messageFormat: "'{0}' on '{1}' does not read as Luau writes it: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoObjectParameter = new(
        id: "SPS005",
        title: "Member does not take the object",
        messageFormat:
            "'{0}' must be static and take the object as its first parameter, of type '{1}', as the other " +
            "members of '{2}' do",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateName = new(
        id: "SPS006",
        title: "Duplicate script API name",
        messageFormat: "'{0}' is declared twice in '{1}'. A script can only mean one of them.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotSettable = new(
        id: "SPS007",
        title: "Property cannot be set through the door",
        messageFormat: "'{0}' names a member of ScriptWrites and cannot be set through it: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Misplaced = new(
        id: "SPS008",
        title: "Script attribute does not belong here",
        messageFormat: "'{0}' cannot be declared here: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor WrongShape = new(
        id: "SPS009",
        title: "Script API member has the wrong shape",
        messageFormat: "'{0}' cannot be bound: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CannotExtend = new(
        id: "SPS010",
        title: "Script class extends what the binding cannot follow",
        messageFormat:
            "'{0}' extends '{1}', and a script would not find the members it is promised: {2}. A class extends " +
            "one declared with [ScriptClass] whose members take the same object.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
