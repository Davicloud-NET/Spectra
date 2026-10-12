using Microsoft.CodeAnalysis;
using SpectraEngine.Entities.Generator.ScriptApi;
using System.Collections.Immutable;

namespace SpectraEngine.Entities.Tests;

// As for the entity generator: a model that holds a symbol still generates
// correct source and never compares equal. And one more rule here: the
// catalogue is behind a Collect, and must not be written again for an edit
// that changes no name.
public sealed class ScriptApiGeneratorCachingTests
{
    [Fact]
    public void An_edit_to_a_file_with_no_script_api_in_it_leaves_every_output_cached()
    {
        SyntaxTree unrelated = GeneratorHarness.Tree(Fixtures.UnrelatedFile, "Unrelated.cs");
        Compilation before = Compile(ScriptApiFixtures.Cached, unrelated);
        GeneratorDriver driver = Ran(before);

        Compilation after = before.ReplaceSyntaxTree(
            unrelated, GeneratorHarness.Tree(Fixtures.UnrelatedFileEdited, "Unrelated.cs"));
        GeneratorRunResult result = RunAgain(driver, after);

        ReasonsFor(result, ScriptApiGenerator.TrackingNames.Models).ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached);
        ReasonsFor(result, ScriptApiGenerator.TrackingNames.Catalog).ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached);
        OutputReasons(result).ShouldNotBeEmpty();
        OutputReasons(result).ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void An_edit_to_the_class_that_changes_nothing_it_declares_leaves_every_output_cached()
    {
        // The transform re-runs for this edit, so only value equality of the
        // model keeps the outputs cached.
        GeneratorRunResult result = Edit(ScriptApiFixtures.Cached, ScriptApiFixtures.CachedWithSpareMember);

        ReasonsFor(result, ScriptApiGenerator.TrackingNames.Models)
            .ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
        OutputReasons(result).ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void An_edit_to_a_summary_writes_the_class_again_and_leaves_the_catalogue_alone()
    {
        GeneratorRunResult result = Edit(ScriptApiFixtures.Cached, ScriptApiFixtures.CachedWithOtherWords);

        ReasonsFor(result, ScriptApiGenerator.TrackingNames.Models).ShouldContain(IncrementalStepRunReason.Modified);
        ReasonsFor(result, ScriptApiGenerator.TrackingNames.Catalog)
            .ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
        OutputReasons(result).Count(reason => reason == IncrementalStepRunReason.Modified).ShouldBe(1);
    }

    [Fact]
    public void An_edit_that_moves_the_class_in_its_file_leaves_the_catalogue_alone()
    {
        GeneratorRunResult result = Edit(ScriptApiFixtures.Cached, ScriptApiFixtures.CachedWithLongerClassSummary);

        ReasonsFor(result, ScriptApiGenerator.TrackingNames.Models).ShouldContain(IncrementalStepRunReason.Modified);
        ReasonsFor(result, ScriptApiGenerator.TrackingNames.Catalog)
            .ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void A_new_member_writes_the_class_and_the_catalogue_again()
    {
        // The control for the tests above: the same kind of edit, and one
        // that adds a name the catalogue numbers.
        GeneratorRunResult result = Edit(ScriptApiFixtures.Cached, ScriptApiFixtures.CachedWithExtraMember);

        ReasonsFor(result, ScriptApiGenerator.TrackingNames.Models).ShouldContain(IncrementalStepRunReason.Modified);
        ReasonsFor(result, ScriptApiGenerator.TrackingNames.Catalog).ShouldContain(IncrementalStepRunReason.Modified);
        OutputReasons(result).Count(reason => reason == IncrementalStepRunReason.Modified).ShouldBe(2);
    }

    private static GeneratorRunResult Edit(string before, string after)
    {
        SyntaxTree tree = GeneratorHarness.Tree(before, "Api.cs");
        Compilation compiled = GeneratorHarness.Compilation(ScriptApiHarness.Stubs(), tree);
        GeneratorDriver driver = Ran(compiled);

        return RunAgain(driver, compiled.ReplaceSyntaxTree(tree, GeneratorHarness.Tree(after, "Api.cs")));
    }

    private static Compilation Compile(string api, SyntaxTree other) =>
        GeneratorHarness.Compilation(ScriptApiHarness.Stubs(), GeneratorHarness.Tree(api, "Api.cs"), other);

    private static GeneratorDriver Ran(Compilation compilation) =>
        ScriptApiHarness.Driver(trackSteps: true).RunGenerators(compilation, TestContext.Current.CancellationToken);

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
