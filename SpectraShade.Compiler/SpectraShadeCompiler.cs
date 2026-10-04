using SpectraEngine.Core;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler.Analysis;
using SpectraShade.Compiler.CodeGen;
using SpectraShade.Compiler.Lexing;
using SpectraShade.Compiler.Syntax;

namespace SpectraShade.Compiler;

/// <summary>Compiles SpectraShade source into per-backend pipeline blobs.</summary>
public sealed class SpectraShadeCompiler : IShaderCompiler
{
    private readonly Dictionary<GraphicsBackend, ICodeGenerator> _generators = [];

    public SpectraShadeCompiler()
    {
        RegisterGenerator(new GlslGenerator());
        RegisterGenerator(new HlslGenerator(GraphicsBackend.D3D11));
        RegisterGenerator(new HlslGenerator(GraphicsBackend.D3D12));
        RegisterGenerator(new SpirVGenerator());
    }

    public void RegisterGenerator(ICodeGenerator generator)
    {
        _generators[generator.Backend] = generator;
    }

    public CompiledShaderFile Compile(string source, ReadOnlySpan<GraphicsBackend> targets)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        var parser = new Parser(tokens);
        var unit = parser.Parse();

        if (parser.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            throw new ShaderCompilationException("Parse errors", parser.Diagnostics);

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(unit))
            throw new ShaderCompilationException("Semantic errors", analyzer.Diagnostics);

        var pipelines = new List<PipelineBlob>();
        var allStages = ShaderStageFlags.None;

        for (int i = 0; i < targets.Length; i++)
        {
            var backend = targets[i];
            if (!_generators.TryGetValue(backend, out var generator))
                throw new InvalidOperationException($"No code generator registered for {backend}");

            var blob = generator.Generate(unit);

            // Instanced variant: same generator over a rewritten AST, so the two
            // vertex stages cannot drift.
            if (InstancedVariant.TryBuild(unit, out CompilationUnit? instancedUnit))
                blob = InstancedBlob.With(blob, generator.Generate(instancedUnit));

            pipelines.Add(blob);
            allStages |= blob.Stages;
        }

        return new CompiledShaderFile
        {
            FormatVersion = EngineInfo.ShaderFormatVersion,
            Stages = allStages,
            Pipelines = pipelines,
        };
    }
}

public sealed class ShaderCompilationException : Exception
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public ShaderCompilationException(string message, IReadOnlyList<Diagnostic> diagnostics)
        : base(FormatMessage(message, diagnostics))
    {
        Diagnostics = diagnostics;
    }

    private static string FormatMessage(string message, IReadOnlyList<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        return $"{message}:\n{string.Join("\n", errors)}";
    }
}
