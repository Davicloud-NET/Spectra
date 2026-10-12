using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Threading;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// What the readers of one container share while its members are read: the
// container, the type of the object its members are of, the names taken so
// far and the diagnostics. Lives for one transform and holds symbols, so
// nothing of it goes into a model.
internal sealed class ScriptReadState
{
    private readonly HashSet<string> _names = new();

    public ScriptReadState(ScriptContainerKind kind, INamedTypeSymbol type, string luauName, CancellationToken token)
    {
        Kind = kind;
        Type = type;
        LuauName = luauName;
        Token = token;
        FullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // The members of a class that is not static are its own.
        if (kind == ScriptContainerKind.Class && !type.IsStatic)
            Self = new ScriptTypeModel(ScriptValueKind.Object, FullTypeName, IsNullable: false, luauName);
    }

    public ScriptContainerKind Kind { get; }

    public INamedTypeSymbol Type { get; }

    public string FullTypeName { get; }

    public string LuauName { get; }

    public CancellationToken Token { get; }

    public ScriptTypeModel? Self { get; private set; }

    public List<DiagnosticInfo> Diagnostics { get; } = new();

    public List<ScriptPropertyModel> Properties { get; } = new();

    public List<ScriptFunctionModel> Functions { get; } = new();

    public List<ScriptOperatorModel> Operators { get; } = new();

    public bool HoldsObjects => Kind is ScriptContainerKind.Class or ScriptContainerKind.Value;

    // The members of a static class or a value take the object first.
    public bool TakesObjectFirst => HoldsObjects && Type.IsStatic;

    // What a script calls the container, for a message.
    public string Described => LuauName.Length > 0 ? LuauName : Type.Name;

    public void Report(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments) =>
        Diagnostics.Add(DiagnosticInfo.Create(descriptor, location, arguments));

    // The name a script calls a member, or null when it is no name or is taken.
    public string? Claim(ISymbol member, AttributeData attribute)
    {
        string name = ScriptAttributes.NamedString(attribute, "Name");
        if (name.Length == 0)
            name = member.Name;

        LocationInfo? location = ScriptAttributes.LocationOf(attribute);
        if (!LuauSignatureText.IsName(name))
        {
            Report(ScriptApiDiagnostics.NotLuau, location, name, member.Name, "a name is letters, digits and underscores");
            return null;
        }

        if (!_names.Add(name))
        {
            Report(ScriptApiDiagnostics.DuplicateName, location, name, Described);
            return null;
        }

        return name;
    }

    // The summary, reported when there is none. A member is still bound
    // without one: the build fails on the diagnostic.
    public string SummaryOf(ISymbol member)
    {
        string summary = DocSummary.Of(member, Token);
        if (summary.Length == 0)
            Report(ScriptApiDiagnostics.NoSummary, LocationInfo.From(member), member.Name);

        return summary;
    }

    // Whether this is the type of the object the container's members are
    // of. The first member to say one decides, and the rest must agree.
    public bool IsSelf(ITypeSymbol type, ISymbol member)
    {
        if (ScriptTypeMap.Of(type) is { IsNullable: false, Kind: not (ScriptValueKind.Number or ScriptValueKind.Boolean or ScriptValueKind.Text or ScriptValueKind.Utf8) } found)
        {
            Self ??= found;
            if (found.ClrType == Self.ClrType)
                return true;
        }

        Report(
            ScriptApiDiagnostics.NoObjectParameter,
            LocationInfo.From(member),
            member.Name,
            Self?.LuauType ?? "Vector3, CFrame, SceneNode or a class with [ScriptClass]",
            Described);
        return false;
    }
}
