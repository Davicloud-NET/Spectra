using Microsoft.CodeAnalysis;
using SpectraEngine.Entities.Generator;
using System.Collections.Immutable;

namespace SpectraEngine.Entities.Tests;

// A model that captures an ISymbol or SyntaxNode still generates correct
// source but never compares equal, so every step re-runs on each keystroke.
// Roslyn reports Cached both for a step that was skipped and for one that
// re-ran and compared equal; the third test tells the two apart.
public sealed class EntityGeneratorCachingTests
{
    [Fact]
    public void An_edit_to_a_file_with_no_entity_in_it_leaves_every_output_cached()
    {
        SyntaxTree entity = GeneratorHarness.Tree(Fixtures.CachedEntity, "Entity.cs");
        SyntaxTree unrelated = GeneratorHarness.Tree(Fixtures.UnrelatedFile, "Unrelated.cs");
        Compilation before = GeneratorHarness.Compilation(entity, unrelated);

        GeneratorDriver driver = GeneratorHarness.Driver(trackSteps: true)
            .RunGenerators(before, TestContext.Current.CancellationToken);

        Compilation after = before.ReplaceSyntaxTree(
            unrelated,
            GeneratorHarness.Tree(Fixtures.UnrelatedFileEdited, "Unrelated.cs"));

        GeneratorRunResult result = RunAgain(driver, after);

        ReasonsFor(result, EntityGenerator.TrackingNames.Models)
            .ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached);
        ReasonsFor(result, EntityGenerator.TrackingNames.AllModels)
            .ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached);

        IncrementalStepRunReason[] outputs = OutputReasons(result);
        outputs.ShouldNotBeEmpty();
        outputs.ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void An_edit_to_the_entity_class_that_changes_nothing_it_declares_leaves_every_output_cached()
    {
        // The transform re-runs for this edit, so only value equality of the
        // model keeps the outputs cached.
        SyntaxTree entity = GeneratorHarness.Tree(Fixtures.CachedEntity, "Entity.cs");
        Compilation before = GeneratorHarness.Compilation(entity);

        GeneratorDriver driver = GeneratorHarness.Driver(trackSteps: true)
            .RunGenerators(before, TestContext.Current.CancellationToken);

        // The spare member comes last, so no span the model records moves.
        Compilation after = before.ReplaceSyntaxTree(
            entity,
            GeneratorHarness.Tree(Fixtures.CachedEntityWithSpareMember, "Entity.cs"));

        GeneratorRunResult result = RunAgain(driver, after);

        ReasonsFor(result, EntityGenerator.TrackingNames.Models)
            .ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached);
        ReasonsFor(result, EntityGenerator.TrackingNames.AllModels)
            .ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached);
        OutputReasons(result).ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void An_edit_to_the_entity_class_that_changes_what_it_declares_is_reported_as_modified()
    {
        // Control for the two tests above: same kind of edit, but one the
        // generator reads.
        SyntaxTree entity = GeneratorHarness.Tree(Fixtures.CachedEntity, "Entity.cs");
        Compilation before = GeneratorHarness.Compilation(entity);

        GeneratorDriver driver = GeneratorHarness.Driver(trackSteps: true)
            .RunGenerators(before, TestContext.Current.CancellationToken);

        Compilation after = before.ReplaceSyntaxTree(
            entity,
            GeneratorHarness.Tree(Fixtures.CachedEntityWithExtraKeyvalue, "Entity.cs"));

        GeneratorRunResult result = RunAgain(driver, after);

        ReasonsFor(result, EntityGenerator.TrackingNames.Models)
            .ShouldContain(IncrementalStepRunReason.Modified);
        OutputReasons(result).ShouldContain(IncrementalStepRunReason.Modified);
    }

    private static GeneratorRunResult RunAgain(GeneratorDriver driver, Compilation compilation) =>
        driver.RunGenerators(compilation, TestContext.Current.CancellationToken)
            .GetRunResult()
            .Results
            .Single();

    private static IncrementalStepRunReason[] OutputReasons(GeneratorRunResult result) => result
        .TrackedOutputSteps
        .SelectMany(step => step.Value)
        .SelectMany(step => step.Outputs)
        .Select(output => output.Reason)
        .ToArray();

    private static IncrementalStepRunReason[] ReasonsFor(GeneratorRunResult result, string step)
    {
        result.TrackedSteps.ContainsKey(step).ShouldBeTrue(
            $"The generator declares a step named '{step}' and the run reported none. " +
            $"Tracked: {string.Join(", ", result.TrackedSteps.Keys)}");

        ImmutableArray<IncrementalGeneratorRunStep> runs = result.TrackedSteps[step];
        return runs
            .SelectMany(run => run.Outputs)
            .Select(output => output.Reason)
            .ToArray();
    }
}
