using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;

namespace SpectraEngine.Entities.Generator;

// A Location as values. A real Location holds its syntax tree, which pins the
// tree in the incremental cache and makes every model compare unequal.
internal readonly record struct LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(SyntaxNode? node) => From(node?.GetLocation());

    // First declaration only.
    public static LocationInfo? From(ISymbol? symbol)
    {
        if (symbol is null || symbol.Locations.Length == 0)
            return null;

        return From(symbol.Locations[0]);
    }

    private static LocationInfo? From(Location? location)
    {
        if (location is null || location.SourceTree is null)
            return null;

        return new LocationInfo(
            location.SourceTree.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }
}

// A diagnostic as values. Decided in the transform, where the symbols are, and
// reported in the output stage, where the SourceProductionContext is.
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> MessageArguments) : IEquatable<DiagnosticInfo>
{
    public static DiagnosticInfo Create(
        DiagnosticDescriptor descriptor,
        LocationInfo? location,
        params string[] messageArguments) =>
        new(descriptor, location, new EquatableArray<string>(messageArguments));

    public Diagnostic ToDiagnostic()
    {
        var arguments = new object?[MessageArguments.Count];
        for (int i = 0; i < MessageArguments.Count; i++)
            arguments[i] = MessageArguments[i];

        return Diagnostic.Create(Descriptor, Location?.ToLocation(), arguments);
    }
}
