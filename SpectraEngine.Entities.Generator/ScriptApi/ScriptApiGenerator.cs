using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SpectraEngine.Entities.Generator.ScriptApi;

/// <summary>
/// Emits the binding behind the script API attributes: for a class with
/// <c>[ScriptClass]</c>, <c>[ScriptValue]</c>, <c>[ScriptLibrary]</c> or
/// <c>[ScriptGlobals]</c>, the code a script's read, write and call of each
/// member come to, and the same declarations as data.
/// </summary>
// Set up like EntityGenerator: attributes are matched by metadata name, a
// model holds values only, and emission is per class. The catalogue is the
// one output behind a Collect, and it reads a cut-down entry, so an edit to
// a summary re-emits one file.
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

        IncrementalValueProvider<ImmutableArray<ScriptCatalogEntry>> catalog = Entries(classes)
            .Combine(Entries(values))
            .Combine(Entries(libraries))
            .Combine(Entries(globals))
            .Select(static (all, _) => all.Left.Left.Left.AddRange(all.Left.Left.Right).AddRange(all.Left.Right).AddRange(all.Right))
            .WithComparer(EntriesComparer.Instance)
            .WithTrackingName(TrackingNames.Catalog);

        context.RegisterSourceOutput(catalog, static (production, entries) => ProduceCatalog(production, entries));
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

    // A class that is not emitted has nothing for the catalogue to name.
    private static IncrementalValueProvider<ImmutableArray<ScriptCatalogEntry>> Entries(
        IncrementalValuesProvider<ScriptContainerModel> models) =>
        models
            .Where(static model => model.IsPartial)
            .Select(static (model, _) => ScriptContainerEmitter.CatalogEntryOf(model))
            .Collect();

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

        var kept = new List<ScriptCatalogEntry>(entries.Length);
        var seen = new HashSet<string>();
        foreach (ScriptCatalogEntry entry in entries)
        {
            // Globals have no name of their own, and several classes may declare some.
            bool isNamed = entry.Kind != ScriptContainerKind.Globals;
            string space = entry.Kind == ScriptContainerKind.Library ? "library " : "class ";
            if (isNamed && !seen.Add(space + entry.LuauName))
            {
                production.ReportDiagnostic(DiagnosticInfo.Create(
                    ScriptApiDiagnostics.DuplicateName, entry.Location, entry.LuauName, "the script API").ToDiagnostic());
                continue;
            }

            kept.Add(entry);
        }

        production.AddSource(ScriptCatalogEmitter.HintName, ScriptCatalogEmitter.Emit(kept));
    }

    // ImmutableArray compares by reference, which would re-emit the
    // catalogue on every edit.
    private sealed class EntriesComparer : IEqualityComparer<ImmutableArray<ScriptCatalogEntry>>
    {
        public static readonly EntriesComparer Instance = new();

        public bool Equals(ImmutableArray<ScriptCatalogEntry> left, ImmutableArray<ScriptCatalogEntry> right)
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

        public int GetHashCode(ImmutableArray<ScriptCatalogEntry> entries) => entries.Length;
    }
}
