using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SpectraEngine.Entities.Generator;

/// <summary>
/// Emits the machinery behind <c>[SpectraEntity]</c>: the keyvalue binder, the
/// input dispatch, the output declarations, a static <c>EntitySchema</c> and the
/// registration into <c>EntityCatalog</c>.
/// </summary>
// Must not reference the engine: attributes are matched by metadata name.
// Emission is per model so one edit re-emits one file. Only the duplicate-name
// check sits behind a Collect. Do not add a CompilationProvider: it would make
// every model depend on every edit in the project.
[Generator(LanguageNames.CSharp)]
public sealed class EntityGenerator : IIncrementalGenerator
{
    /// <summary>
    /// The names the incremental steps are tracked under, so a test can assert
    /// that an unrelated edit changed nothing.
    /// </summary>
    public static class TrackingNames
    {
        /// <summary>The per-class value model, after the symbols are dropped.</summary>
        public const string Models = "EntityModels";

        /// <summary>The batch the duplicate-name check reads.</summary>
        public const string AllModels = "AllEntityModels";
    }

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<EntityModel> models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EntityModelFactory.EntityAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (syntaxContext, token) => EntityModelFactory.Create(syntaxContext, token))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .WithTrackingName(TrackingNames.Models);

        context.RegisterSourceOutput(models, static (production, model) => Produce(production, model));

        context.RegisterSourceOutput(
            models.Collect().WithTrackingName(TrackingNames.AllModels),
            static (production, all) => ReportDuplicates(production, all));
    }

    private static void Produce(SourceProductionContext production, EntityModel model)
    {
        for (int i = 0; i < model.Diagnostics.Count; i++)
            production.ReportDiagnostic(model.Diagnostics[i].ToDiagnostic());

        // A non-partial class cannot be reopened. Every other refusal only
        // drops the offending member.
        if (!model.IsPartial)
            return;

        production.AddSource(EntityEmitter.HintName(model), EntityEmitter.Emit(model));
    }

    private static void ReportDuplicates(SourceProductionContext production, ImmutableArray<EntityModel> models)
    {
        if (models.Length < 2)
            return;

        var seen = new Dictionary<string, EntityModel>(models.Length);
        foreach (EntityModel model in models)
        {
            if (model.ClassName.Length == 0)
                continue;

            if (seen.TryGetValue(model.ClassName, out EntityModel first))
            {
                // Reported on the second declaration only.
                production.ReportDiagnostic(DiagnosticInfo.Create(
                    EntityDiagnostics.DuplicateClassName,
                    model.Location,
                    model.ClassName,
                    first.FullTypeName,
                    model.FullTypeName).ToDiagnostic());
                continue;
            }

            seen.Add(model.ClassName, model);
        }
    }
}
