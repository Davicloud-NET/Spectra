using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SpectraEngine.Entities.Generator;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace SpectraEngine.Entities.Tests;

// Runs EntityGenerator over an in-memory compilation.
internal static class GeneratorHarness
{
    private static readonly MetadataReference[] References = LoadReferences();

    public static SyntaxTree Tree(string source, string path) =>
        CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: path);

    public static CSharpCompilation Compilation(params SyntaxTree[] trees) =>
        CSharpCompilation.Create(
            "EntityFixtures",
            trees,
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

    // trackSteps makes the driver keep every intermediate table.
    public static GeneratorDriver Driver(bool trackSteps = false) =>
        CSharpGeneratorDriver.Create(
            generators: [new EntityGenerator().AsSourceGenerator()],
            additionalTexts: [],
            parseOptions: null,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: trackSteps));

    public static GeneratorRun Run(string source) => Run(Tree(source, "Fixture.cs"));

    public static GeneratorRun Run(params SyntaxTree[] trees)
    {
        GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(
            Compilation(trees), out Compilation output, out _, TestContext.Current.CancellationToken);

        return new GeneratorRun(driver.GetRunResult(), output);
    }

    private static MetadataReference[] LoadReferences()
    {
        // This process's own list: the framework plus SpectraEngine.Core.
        string assemblies = (string)(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "");
        var references = new List<MetadataReference>();
        foreach (string path in assemblies.Split(Path.PathSeparator))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return references.ToArray();
    }
}

internal sealed class GeneratorRun
{
    private readonly GeneratorDriverRunResult _result;
    private readonly Compilation _output;

    public GeneratorRun(GeneratorDriverRunResult result, Compilation output)
    {
        _result = result;
        _output = output;
    }

    public ImmutableArray<Diagnostic> Diagnostics => _result.Diagnostics;

    public string[] DiagnosticIds => _result.Diagnostics.Select(d => d.Id).ToArray();

    public int SourceCount => _result.GeneratedTrees.Length;

    public string OnlySource()
    {
        _result.GeneratedTrees.Length.ShouldBe(1, Describe());
        return _result.GeneratedTrees[0].GetText().ToString();
    }

    // Errors from compiling the fixture together with the generated source.
    public string[] CompileErrors() => _output
        .GetDiagnostics()
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .Select(d => $"{d.Id} {d.GetMessage()}")
        .ToArray();

    public string Describe() => _result.Diagnostics.Length == 0
        ? "(the generator reported nothing)"
        : string.Join(Environment.NewLine, _result.Diagnostics.Select(d => $"{d.Id}: {d.GetMessage()}"));
}
