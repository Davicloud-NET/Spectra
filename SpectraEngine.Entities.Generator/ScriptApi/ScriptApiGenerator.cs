using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SpectraEngine.Entities.Generator.ScriptApi;

/// <summary>
/// Emits the binding behind the script API attributes: for a class with
/// <c>[ScriptClass]</c>, <c>[ScriptValue]</c>, <c>[ScriptLibrary]</c> or
/// <c>[ScriptGlobals]</c>, the code a script's read, write and call of each
/// member come to, and the same declarations as data.
/// </summary>
// Set up like EntityGenerator: attributes are matched by metadata name, a
// model holds values only, and emission is per class. The catalogue is
// behind a Collect and reads a cut-down entry with no place in it, so an
// edit that changes no name re-emits one file.
[Generator(LanguageNames.CSharp)]
public sealed class ScriptApiGenerator : IIncrementalGenerator
{
    /// <summary>
    /// The names the incremental steps are tracked under, so a test can assert
    /// that an unrelated edit changed nothing.
    /// </summary>
    public static class TrackingNames
    {
        /// <summary>The per-class value model, after the symbols are dropped.</summary>
        public const string Models = "ScriptApiModels";

        /// <summary>What the catalogue reads of every class.</summary>
        public const string Catalog = "ScriptApiCatalog";
    }

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ScriptContainerModel> classes = Models(context, ScriptTypeMap.ClassAttribute, ScriptContainerKind.Class);
        IncrementalValuesProvider<ScriptContainerModel> values = Models(context, ScriptAttributes.Value, ScriptContainerKind.Value);
        IncrementalValuesProvider<ScriptContainerModel> libraries = Models(context, ScriptAttributes.Library, ScriptContainerKind.Library);
        IncrementalValuesProvider<ScriptContainerModel> globals = Models(context, ScriptAttributes.Globals, ScriptContainerKind.Globals);

        context.RegisterSourceOutput(classes, static (production, model) => Produce(production, model));
        context.RegisterSourceOutput(values, static (production, model) => Produce(production, model));
        context.RegisterSourceOutput(libraries, static (production, model) => Produce(production, model));
        context.RegisterSourceOutput(globals, static (production, model) => Produce(production, model));

        var kinds = new[] { classes, values, libraries, globals };
        context.RegisterSourceOutput(
            Gathered(kinds, ScriptContainerEmitter.CatalogEntryOf).WithTrackingName(TrackingNames.Catalog),
            static (production, entries) => ProduceCatalog(production, entries));

        // A place moves with every edit above it. The errors that need one
        // are reported apart from the catalogue, which so stays cached.
        context.RegisterSourceOutput(
            Gathered(kinds, ScriptContainerEmitter.PlaceOf),
            static (production, places) => ReportAcrossClasses(production, places));
    }

    private static IncrementalValuesProvider<ScriptContainerModel> Models(
        IncrementalGeneratorInitializationContext context, string attribute, ScriptContainerKind kind) =>
        context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attribute,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: (syntaxContext, token) => ScriptModelFactory.Create(syntaxContext, kind, token))
            .SelectMany(static (model, _) =>
                model is null ? ImmutableArray<ScriptContainerModel>.Empty : ImmutableArray.Create(model))
            .WithTrackingName(TrackingNames.Models);

    // One value of every emitted container, the kinds in the order given.
    // A class that is not emitted has nothing for the catalogue to name.
    private static IncrementalValueProvider<ImmutableArray<T>> Gathered<T>(
        IncrementalValuesProvider<ScriptContainerModel>[] kinds, Func<ScriptContainerModel, T> of)
        where T : IEquatable<T>
    {
        IncrementalValueProvider<ImmutableArray<T>> all = default;
        for (int i = 0; i < kinds.Length; i++)
        {
            IncrementalValueProvider<ImmutableArray<T>> kind = kinds[i]
                .Where(static model => model.IsPartial)
                .Select((model, _) => of(model))
                .Collect();

            all = i == 0 ? kind : all.Combine(kind).Select(static (both, _) => both.Left.AddRange(both.Right));
        }

        return all.WithComparer(ByItems<T>.Instance);
    }

    private static void Produce(SourceProductionContext production, ScriptContainerModel model)
    {
        for (int i = 0; i < model.Diagnostics.Count; i++)
            production.ReportDiagnostic(model.Diagnostics[i].ToDiagnostic());

        // A class that is not partial cannot be reopened, and one whose
        // attribute does not belong on it has no binding to write.
        if (!model.IsPartial)
            return;

        production.AddSource(ScriptContainerEmitter.HintName(model), ScriptContainerEmitter.Emit(model));
    }

    private static void ProduceCatalog(SourceProductionContext production, ImmutableArray<ScriptCatalogEntry> entries)
    {
        if (entries.Length == 0)
            return;

        production.AddSource(ScriptCatalogEmitter.HintName, ScriptCatalogEmitter.Emit(entries));
    }

    // What only all containers together show: a name another one has
    // taken, and a class that extends what the binding cannot follow.
    private static void ReportAcrossClasses(SourceProductionContext production, ImmutableArray<ScriptContainerPlace> places)
    {
        List<ScriptCatalogEntry> listed = ScriptCatalogEntry.Listed(places.Select(place => place.Entry));
        var seen = new HashSet<string>();
        foreach (ScriptContainerPlace place in places)
        {
            ScriptCatalogEntry entry = place.Entry;
            if (entry.NameKey is { } key && !seen.Add(key))
                Report(production, ScriptApiDiagnostics.DuplicateName, place, entry.LuauName, "the script API");
            else if (ScriptExtends.BaseOf(entry, listed, out string? why) is null && why is not null)
                Report(production, ScriptApiDiagnostics.CannotExtend, place, entry.LuauName, entry.Extends, why);
        }
    }

    private static void Report(
        SourceProductionContext production, DiagnosticDescriptor descriptor, ScriptContainerPlace place, params string[] arguments) =>
        production.ReportDiagnostic(DiagnosticInfo.Create(descriptor, place.Location, arguments).ToDiagnostic());

    // ImmutableArray compares by reference, which would run the output
    // again on every edit.
    private sealed class ByItems<T> : IEqualityComparer<ImmutableArray<T>>
        where T : IEquatable<T>
    {
        public static readonly ByItems<T> Instance = new();

        public bool Equals(ImmutableArray<T> left, ImmutableArray<T> right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (!left[i].Equals(right[i]))
                    return false;
            }

            return true;
        }

        public int GetHashCode(ImmutableArray<T> items) => items.Length;
    }
}
