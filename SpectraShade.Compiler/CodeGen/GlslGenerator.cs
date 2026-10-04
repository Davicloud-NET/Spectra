using System.Globalization;
using System.Text;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler.Analysis;
using SpectraShade.Compiler.Lexing;
using SpectraShade.Compiler.Syntax;

namespace SpectraShade.Compiler.CodeGen;

/// <summary>
/// Generates GLSL source from a SpectraShade AST. Stages target 330 core;
/// a stage's #version rises only when a feature it emits needs it.
/// </summary>
public sealed class GlslGenerator : ICodeGenerator
{
    public GraphicsBackend Backend => GraphicsBackend.OpenGL;
    public ShaderDataFormat OutputFormat => ShaderDataFormat.SourceText;

    private CompilationUnit _unit = null!;

    // Set before each stage is emitted.
    private bool _isVertex;
    private bool _isGeometry;
    private bool _isCompute;
    private StructDeclaration? _inputStruct;
    private string? _inputParam;

    // Set while emitting a [Geometry] body. Output field names assigned there
    // become g_* varyings; the [Position] field becomes gl_Position.
    private string? _geomPositionField;
    private HashSet<string>? _geomOutputFieldNames;

    // "g_" when a geometry stage feeds the fragment stage.
    private string _fragmentVaryingPrefix = "v_";

    // GLSL entries are void main(), so `return expr;` in a stage body assigns the
    // stage outputs and then returns bare. None while emitting helper functions.
    private enum StageReturnMode { None, Vertex, Fragment }
    private StageReturnMode _returnMode;
    private StructDeclaration? _stageReturnStruct;
    private int _returnTempCounter;

    private int _minVersion;

    // Resolves `var` declarations to concrete GLSL types.
    private readonly TypeInference _types = new();

    // GLSL 4.6 reserved words (spec 3.6). User identifiers matching one get an _ss_ prefix.
    private static readonly HashSet<string> GlslReservedWords = new(StringComparer.Ordinal)
    {
        "input", "output", "common", "partition", "active", "asm", "class", "union",
        "enum", "typedef", "template", "this", "resource", "goto", "inline", "noinline",
        "public", "static", "extern", "external", "interface", "long", "short", "half",
        "fixed", "unsigned", "superp", "hvec2", "hvec3", "hvec4", "fvec2", "fvec3", "fvec4",
        "filter", "sizeof", "cast", "namespace", "using", "sampler3DRect",
        "attribute", "varying", "subroutine", "patch", "sample", "coherent", "volatile",
        "restrict", "readonly", "writeonly", "noperspective", "centroid", "precise",
    };

    // Names the generator invents for the current stage: fragColor, a_*, v_*, g_*.
    // A user local with the same name would shadow the interface variable and the
    // output would never be written, so those get the _ss_ escape too.
    // Uniform names are not tracked; shadowing a uniform with a local is legal.
    private readonly HashSet<string> _stageOwnedNames = new(StringComparer.Ordinal);

    private string EscapeId(string name)
        => GlslReservedWords.Contains(name) || _stageOwnedNames.Contains(name) ? "_ss_" + name : name;

    private static readonly Dictionary<string, string> MathBuiltins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Normalize"] = "normalize",
        ["Dot"] = "dot",
        ["Cross"] = "cross",
        ["Length"] = "length",
        ["Distance"] = "distance",
        ["Mix"] = "mix",
        ["Lerp"] = "mix",
        ["Clamp"] = "clamp",
        ["Min"] = "min",
        ["Max"] = "max",
        ["Abs"] = "abs",
        ["Floor"] = "floor",
        ["Ceil"] = "ceil",
        ["Fract"] = "fract",
        ["Mod"] = "mod",
        ["Pow"] = "pow",
        ["Sqrt"] = "sqrt",
        ["Sin"] = "sin",
        ["Cos"] = "cos",
        ["Tan"] = "tan",
        ["Asin"] = "asin",
        ["Acos"] = "acos",
        ["Atan"] = "atan",
        ["Reflect"] = "reflect",
        ["Refract"] = "refract",
        ["Step"] = "step",
        ["SmoothStep"] = "smoothstep",
        ["Sign"] = "sign",
        ["Exp"] = "exp",
        ["Log"] = "log",
        ["Exp2"] = "exp2",
        ["Log2"] = "log2",
        ["Inverse"] = "inverse",
        ["Transpose"] = "transpose",
        ["Determinant"] = "determinant",
    };

    public PipelineBlob Generate(CompilationUnit unit)
    {
        _unit = unit;
        var shader = unit.Shader;

        byte[]? vertexData = null;
        byte[]? fragmentData = null;
        var stages = ShaderStageFlags.None;

        var functions = shader.Members.OfType<FunctionDeclaration>().ToList();
        var cbuffers = shader.Members.OfType<CBufferDeclaration>().ToList();
        var samplers = shader.Members.OfType<SamplerDeclaration>().ToList();

        var vertexFunc = functions.FirstOrDefault(f => f.HasAttribute("Vertex"));
        var fragmentFunc = functions.FirstOrDefault(f => f.HasAttribute("Fragment"));
        var geometryFunc = functions.FirstOrDefault(f => f.HasAttribute("Geometry"));
        var computeFunc = functions.FirstOrDefault(f => f.HasAttribute("Compute"));
        var helperFunctions = functions.Where(f =>
            !f.HasAttribute("Vertex") && !f.HasAttribute("Fragment")
            && !f.HasAttribute("Geometry") && !f.HasAttribute("Compute")).ToList();

        var allStructs = new List<StructDeclaration>(unit.Structs);
        allStructs.AddRange(shader.Members.OfType<StructDeclaration>());
        _types.Configure(allStructs, helperFunctions);

        byte[]? geometryData = null;
        byte[]? computeData = null;

        if (vertexFunc is not null)
        {
            vertexData = Encoding.UTF8.GetBytes(
                EmitVertexStage(vertexFunc, cbuffers, samplers, helperFunctions, allStructs));
            stages |= ShaderStageFlags.Vertex;
        }

        if (geometryFunc is not null)
        {
            geometryData = Encoding.UTF8.GetBytes(
                EmitGeometryStage(geometryFunc, vertexFunc, cbuffers, samplers, helperFunctions, allStructs));
            stages |= ShaderStageFlags.Geometry;
        }

        if (fragmentFunc is not null)
        {
            fragmentData = Encoding.UTF8.GetBytes(
                EmitFragmentStage(fragmentFunc, geometryFunc is not null, cbuffers, samplers, helperFunctions, allStructs));
            stages |= ShaderStageFlags.Fragment;
        }

        if (computeFunc is not null)
        {
            computeData = Encoding.UTF8.GetBytes(
                EmitComputeStage(computeFunc, cbuffers, samplers, helperFunctions, allStructs));
            stages |= ShaderStageFlags.Compute;
        }

        return new PipelineBlob
        {
            Backend = Backend,
            Format = OutputFormat,
            Stages = stages,
            VertexData = vertexData,
            FragmentData = fragmentData,
            GeometryData = geometryData,
            ComputeData = computeData,
            VertexInputs = VertexInputLayout.DescribeFor(vertexFunc, allStructs),
        };
    }

    private string EmitVertexStage(
        FunctionDeclaration func,
        List<CBufferDeclaration> cbuffers,
        List<SamplerDeclaration> samplers,
        List<FunctionDeclaration> helpers,
        List<StructDeclaration> structs)
    {
        _minVersion = 330;
        var sb = new StringBuilder();

        _types.Reset();
        _types.DeclareGlobals(cbuffers, samplers);

        StructDeclaration? inputStruct = null;
        string inputParamName = "input";
        if (func.Parameters.Count > 0)
        {
            inputStruct = FindStruct(func.Parameters[0].Type.Name, structs);
            inputParamName = func.Parameters[0].Name;
        }
        var returnStruct = FindStruct(func.ReturnType.Name, structs);

        // Fill before anything is emitted.
        _stageOwnedNames.Clear();
        if (inputStruct is not null)
            foreach (var field in inputStruct.Fields)
                _stageOwnedNames.Add($"a_{field.Name}");
        if (returnStruct is not null)
            foreach (var field in returnStruct.Fields)
                if (!HasAttribute(field.Attributes, "Position"))
                    _stageOwnedNames.Add($"v_{field.Name}");

        EmitStructs(sb, structs);

        if (inputStruct is not null)
        {
            for (int i = 0; i < inputStruct.Fields.Count; i++)
            {
                var field = inputStruct.Fields[i];
                // A [Location] with no literal argument falls back to the field index.
                string layout = GetAttribute(field.Attributes, "Location") is not null
                    ? $"layout(location = {VertexInputLayout.ResolveLocation(field, i)}) "
                    : "";
                sb.AppendLine($"{layout}in {GlslType(field.Type.Name)} a_{field.Name};");
            }
            sb.AppendLine();
        }

        if (returnStruct is not null)
        {
            foreach (var field in returnStruct.Fields)
            {
                if (HasAttribute(field.Attributes, "Position"))
                    continue; // written through gl_Position
                sb.AppendLine($"out {GlslType(field.Type.Name)} v_{field.Name};");
            }
            sb.AppendLine();
        }

        EmitUniforms(sb, cbuffers);
        EmitSamplerUniforms(sb, samplers);

        foreach (var helper in helpers)
            EmitFunction(sb, helper);

        sb.AppendLine("void main()");
        sb.AppendLine("{");
        EmitVertexBody(sb, func, returnStruct, inputStruct, inputParamName, 1);
        sb.AppendLine("}");

        return FinishStage(sb);
    }

    private string EmitFragmentStage(
        FunctionDeclaration func,
        bool hasGeometry,
        List<CBufferDeclaration> cbuffers,
        List<SamplerDeclaration> samplers,
        List<FunctionDeclaration> helpers,
        List<StructDeclaration> structs)
    {
        _minVersion = 330;
        var sb = new StringBuilder();

        _types.Reset();
        _types.DeclareGlobals(cbuffers, samplers);

        // GLSL links stages by varying name.
        _fragmentVaryingPrefix = hasGeometry ? "g_" : "v_";

        StructDeclaration? inputStruct = null;
        string inputParamName = "input";
        if (func.Parameters.Count > 0)
        {
            inputStruct = FindStruct(func.Parameters[0].Type.Name, structs);
            inputParamName = func.Parameters[0].Name;
        }
        var returnStruct = FindStruct(func.ReturnType.Name, structs);

        // Fill before anything is emitted.
        _stageOwnedNames.Clear();
        if (inputStruct is not null)
            foreach (var field in inputStruct.Fields)
                if (!HasAttribute(field.Attributes, "Position"))
                    _stageOwnedNames.Add($"{_fragmentVaryingPrefix}{field.Name}");
        // A void fragment stage writes depth only.
        if (returnStruct is null && func.ReturnType.Name != "void")
            _stageOwnedNames.Add("fragColor");

        EmitStructs(sb, structs);

        if (inputStruct is not null)
        {
            foreach (var field in inputStruct.Fields)
            {
                if (HasAttribute(field.Attributes, "Position"))
                    continue;
                sb.AppendLine($"in {GlslType(field.Type.Name)} {_fragmentVaryingPrefix}{field.Name};");
            }
            sb.AppendLine();
        }

        // Both depth layout qualifiers need GLSL 4.20.
        if (func.HasAttribute("EarlyDepthStencil"))
        {
            Require(420);
            sb.AppendLine("layout(early_fragment_tests) in;");
        }

        var depthWriteAttr = GetAttribute(func.Attributes, "DepthWrite");
        if (depthWriteAttr is not null)
        {
            Require(420);
            string depthCondition = "depth_any";
            if (depthWriteAttr.Arguments.Count > 0 && depthWriteAttr.Arguments[0] is IdentifierExpression depthId)
            {
                depthCondition = depthId.Name switch
                {
                    "Less" => "depth_less",
                    "Greater" => "depth_greater",
                    "Unchanged" => "depth_unchanged",
                    _ => "depth_any",
                };
            }
            sb.AppendLine($"layout({depthCondition}) out float gl_FragDepth;");
        }

        if (func.HasAttribute("EarlyDepthStencil") || depthWriteAttr is not null)
            sb.AppendLine();

        if (returnStruct is not null)
        {
            for (int i = 0; i < returnStruct.Fields.Count; i++)
            {
                var field = returnStruct.Fields[i];
                // A [Target] with no literal argument falls back to the field index.
                string layout = GetAttribute(field.Attributes, "Target") is not null
                    ? $"layout(location = {GetIntArg(field.Attributes, "Target", i)}) "
                    : "";
                sb.AppendLine($"{layout}out {GlslType(field.Type.Name)} {EscapeId(field.Name)};");
            }
        }
        else if (func.ReturnType.Name != "void")
        {
            sb.AppendLine($"out {GlslType(func.ReturnType.Name)} fragColor;");
        }
        sb.AppendLine();

        EmitUniforms(sb, cbuffers);
        EmitSamplerUniforms(sb, samplers);

        foreach (var helper in helpers)
            EmitFunction(sb, helper);

        sb.AppendLine("void main()");
        sb.AppendLine("{");
        EmitFragmentBody(sb, func, inputStruct, inputParamName, returnStruct, 1);
        sb.AppendLine("}");

        return FinishStage(sb);
    }

    private string EmitGeometryStage(
        FunctionDeclaration func,
        FunctionDeclaration? vertexFunc,
        List<CBufferDeclaration> cbuffers,
        List<SamplerDeclaration> samplers,
        List<FunctionDeclaration> helpers,
        List<StructDeclaration> structs)
    {
        _minVersion = 330;
        var sb = new StringBuilder();

        _types.Reset();
        _types.DeclareGlobals(cbuffers, samplers);

        string inputPrimitive = "triangles";
        var inputPrimAttr = GetAttribute(func.Attributes, "InputPrimitive");
        if (inputPrimAttr is not null && inputPrimAttr.Arguments.Count > 0
            && inputPrimAttr.Arguments[0] is IdentifierExpression inputPrimId)
        {
            inputPrimitive = inputPrimId.Name switch
            {
                "Points" => "points",
                "Lines" => "lines",
                "LinesAdjacency" => "lines_adjacency",
                "Triangles" => "triangles",
                "TrianglesAdjacency" => "triangles_adjacency",
                _ => "triangles",
            };
        }
        sb.AppendLine($"layout({inputPrimitive}) in;");

        string outputPrimitive = "triangle_strip";
        var outputPrimAttr = GetAttribute(func.Attributes, "OutputPrimitive");
        if (outputPrimAttr is not null && outputPrimAttr.Arguments.Count > 0
            && outputPrimAttr.Arguments[0] is IdentifierExpression outputPrimId)
        {
            outputPrimitive = outputPrimId.Name switch
            {
                "Points" => "points",
                "LineStrip" => "line_strip",
                "TriangleStrip" => "triangle_strip",
                _ => "triangle_strip",
            };
        }

        string maxVerts = GetIntArg(func.Attributes, "MaxVertexCount", 3).ToString(CultureInfo.InvariantCulture);
        sb.AppendLine($"layout({outputPrimitive}, max_vertices = {maxVerts}) out;");
        sb.AppendLine();

        // Inputs are loose arrays under the vertex stage's v_* names: an interface
        // block on one side and loose varyings on the other never link. Hence
        // also the vertex return struct over the declared parameter type.
        var declaredInput = func.Parameters.Count > 0 ? FindStruct(func.Parameters[0].Type.Name, structs) : null;
        var vertexOutput = vertexFunc is not null ? FindStruct(vertexFunc.ReturnType.Name, structs) : null;
        var inputStruct = declaredInput is not null ? (vertexOutput ?? declaredInput) : null;
        string inputParamName = func.Parameters.Count > 0 ? func.Parameters[0].Name : "vertices";

        // Outputs are g_* varyings from the vertex return struct.
        var outputStruct = vertexOutput ?? inputStruct;

        // Fill before anything is emitted.
        _stageOwnedNames.Clear();
        if (inputStruct is not null)
            foreach (var field in inputStruct.Fields)
                if (!HasAttribute(field.Attributes, "Position"))
                    _stageOwnedNames.Add($"v_{field.Name}");
        if (outputStruct is not null)
            foreach (var field in outputStruct.Fields)
                if (!HasAttribute(field.Attributes, "Position"))
                    _stageOwnedNames.Add($"g_{field.Name}");

        EmitStructs(sb, structs);

        if (inputStruct is not null)
        {
            foreach (var field in inputStruct.Fields)
            {
                if (HasAttribute(field.Attributes, "Position"))
                    continue; // read through gl_in[i].gl_Position
                sb.AppendLine($"in {GlslType(field.Type.Name)} v_{field.Name}[];");
            }
            sb.AppendLine();
        }

        if (outputStruct is not null)
        {
            foreach (var field in outputStruct.Fields)
            {
                if (HasAttribute(field.Attributes, "Position"))
                    continue; // written through gl_Position
                sb.AppendLine($"out {GlslType(field.Type.Name)} g_{field.Name};");
            }
            sb.AppendLine();
        }

        EmitUniforms(sb, cbuffers);
        EmitSamplerUniforms(sb, samplers);

        foreach (var helper in helpers)
            EmitFunction(sb, helper);

        sb.AppendLine("void main()");
        sb.AppendLine("{");
        EmitGeometryBody(sb, func, inputStruct, inputParamName, outputStruct, 1);
        sb.AppendLine("}");

        return FinishStage(sb);
    }

    private string EmitComputeStage(
        FunctionDeclaration func,
        List<CBufferDeclaration> cbuffers,
        List<SamplerDeclaration> samplers,
        List<FunctionDeclaration> helpers,
        List<StructDeclaration> structs)
    {
        // Compute needs GLSL 4.30.
        _minVersion = 430;
        var sb = new StringBuilder();

        _types.Reset();
        _types.DeclareGlobals(cbuffers, samplers);

        _stageOwnedNames.Clear();

        var numThreadsAttr = GetAttribute(func.Attributes, "NumThreads");
        string x = "1", y = "1", z = "1";
        if (numThreadsAttr is not null)
        {
            if (numThreadsAttr.Arguments.Count >= 1)
                x = EmitExpression(numThreadsAttr.Arguments[0]);
            if (numThreadsAttr.Arguments.Count >= 2)
                y = EmitExpression(numThreadsAttr.Arguments[1]);
            if (numThreadsAttr.Arguments.Count >= 3)
                z = EmitExpression(numThreadsAttr.Arguments[2]);
        }
        sb.AppendLine($"layout(local_size_x = {x}, local_size_y = {y}, local_size_z = {z}) in;");
        sb.AppendLine();

        EmitUniforms(sb, cbuffers);
        EmitSamplerUniforms(sb, samplers);

        foreach (var helper in helpers)
            EmitFunction(sb, helper);

        sb.AppendLine("void main()");
        sb.AppendLine("{");
        EmitComputeBody(sb, func, 1);
        sb.AppendLine("}");

        return FinishStage(sb);
    }

    private void EmitVertexBody(StringBuilder sb, FunctionDeclaration func, StructDeclaration? returnStruct,
        StructDeclaration? inputStruct, string inputParam, int indent)
    {
        SetStageContext(isVertex: true, inputStruct: inputStruct, inputParam: inputParam);
        _returnMode = StageReturnMode.Vertex;
        _stageReturnStruct = returnStruct;
        _returnTempCounter = 0;
        foreach (var p in func.Parameters)
            _types.Declare(p.Name, p.Type.Name);

        foreach (var stmt in func.Body.Statements)
            EmitStatement(sb, stmt, indent);

        _returnMode = StageReturnMode.None;
        _stageReturnStruct = null;
        SetStageContext();
    }

    private void EmitFragmentBody(StringBuilder sb, FunctionDeclaration func, StructDeclaration? inputStruct, string inputParam, StructDeclaration? returnStruct, int indent)
    {
        SetStageContext(inputStruct: inputStruct, inputParam: inputParam);
        _returnMode = StageReturnMode.Fragment;
        _stageReturnStruct = returnStruct;
        _returnTempCounter = 0;
        foreach (var p in func.Parameters)
            _types.Declare(p.Name, p.Type.Name);

        foreach (var stmt in func.Body.Statements)
            EmitStatement(sb, stmt, indent);

        _returnMode = StageReturnMode.None;
        _stageReturnStruct = null;
        SetStageContext();
    }

    private void SetStageContext(bool isVertex = false, bool isGeometry = false, bool isCompute = false,
        StructDeclaration? inputStruct = null, string? inputParam = null)
    {
        _isVertex = isVertex;
        _isGeometry = isGeometry;
        _isCompute = isCompute;
        _inputStruct = inputStruct;
        _inputParam = inputParam;
    }

    private void EmitGeometryBody(StringBuilder sb, FunctionDeclaration func, StructDeclaration? inputStruct, string inputParam, StructDeclaration? outputStruct, int indent)
    {
        SetStageContext(isGeometry: true, inputStruct: inputStruct, inputParam: inputParam);
        _geomPositionField = outputStruct?.Fields
            .FirstOrDefault(f => HasAttribute(f.Attributes, "Position"))?.Name;
        _geomOutputFieldNames = outputStruct is not null
            ? new HashSet<string>(outputStruct.Fields.Select(f => f.Name), StringComparer.Ordinal)
            : null;
        foreach (var p in func.Parameters)
            _types.Declare(p.Name, p.Type.Name);
        foreach (var stmt in func.Body.Statements)
            EmitStatement(sb, stmt, indent);
        _geomPositionField = null;
        _geomOutputFieldNames = null;
        SetStageContext();
    }

    private void EmitComputeBody(StringBuilder sb, FunctionDeclaration func, int indent)
    {
        SetStageContext(isCompute: true);
        _types.Declare("GlobalInvocationID", "uvec3");
        _types.Declare("LocalInvocationID", "uvec3");
        _types.Declare("WorkGroupID", "uvec3");
        _types.Declare("LocalInvocationIndex", "uint");
        foreach (var p in func.Parameters)
            _types.Declare(p.Name, p.Type.Name);
        foreach (var stmt in func.Body.Statements)
            EmitStatement(sb, stmt, indent);
        SetStageContext();
    }

    private void EmitStructs(StringBuilder sb, List<StructDeclaration> structs)
    {
        foreach (var s in structs)
        {
            sb.AppendLine($"struct {s.Name}");
            sb.AppendLine("{");
            foreach (var field in s.Fields)
                sb.AppendLine($"    {GlslType(field.Type.Name)} {EscapeId(field.Name)}{EmitArraySuffix(field.Type)};");
            sb.AppendLine("};");
            sb.AppendLine();
        }
    }

    private void EmitUniforms(StringBuilder sb, List<CBufferDeclaration> cbuffers)
    {
        foreach (var cbuffer in cbuffers)
        {
            sb.AppendLine($"// cbuffer {cbuffer.Name}");
            foreach (var field in cbuffer.Fields)
                sb.AppendLine($"uniform {GlslType(field.Type.Name)} {EscapeId(field.Name)}{EmitArraySuffix(field.Type)};");
            sb.AppendLine();
        }
    }

    private void EmitSamplerUniforms(StringBuilder sb, List<SamplerDeclaration> samplers)
    {
        foreach (var sampler in samplers)
            sb.AppendLine($"uniform {GlslType(sampler.Type.Name)} {EscapeId(sampler.Name)}{EmitArraySuffix(sampler.Type)};");
        if (samplers.Count > 0)
            sb.AppendLine();
    }

    private void EmitFunction(StringBuilder sb, FunctionDeclaration func)
    {
        string ret = GlslType(func.ReturnType.Name);
        string parms = string.Join(", ", func.Parameters.Select(p => $"{GlslType(p.Type.Name)} {EscapeId(p.Name)}"));
        sb.AppendLine($"{ret} {func.Name}({parms})");

        var saved = _types.Snapshot();
        foreach (var p in func.Parameters)
            _types.Declare(p.Name, p.Type.Name);
        EmitBlock(sb, func.Body, 0);
        _types.Restore(saved);

        sb.AppendLine();
    }

    private void EmitBlock(StringBuilder sb, BlockStatement block, int indent)
    {
        string pad = new(' ', indent * 4);
        sb.AppendLine($"{pad}{{");
        foreach (var stmt in block.Statements)
            EmitStatement(sb, stmt, indent + 1);
        sb.AppendLine($"{pad}}}");
    }

    private void EmitStatement(StringBuilder sb, SyntaxNode node, int indent)
    {
        string pad = new(' ', indent * 4);

        switch (node)
        {
            case VariableDeclaration v:
                // GLSL has no auto. Unknown `var` types fall back to float, as in the HLSL generator.
                string specType = v.Type.Name == "var"
                    ? _types.Infer(v.Initializer!) ?? "float"
                    : v.Type.Name;
                string varType = GlslType(specType);
                _types.Declare(v.Name, specType);

                // GLSL has no zero-argument struct constructor, so `new T()` gets no initializer.
                string init;
                if (v.Initializer is NewExpression ne && ne.Arguments.Count == 0)
                    init = "";
                else
                    init = v.Initializer is not null ? $" = {EmitExpression(v.Initializer)}" : "";

                sb.AppendLine($"{pad}{varType} {EscapeId(v.Name)}{EmitArraySuffix(v.Type)}{init};");
                break;

            case ReturnStatement r:
                if (_returnMode != StageReturnMode.None)
                {
                    EmitStageReturn(sb, r, pad);
                    break;
                }
                string val = r.Value is not null ? $" {EmitExpression(r.Value)}" : "";
                sb.AppendLine($"{pad}return{val};");
                break;

            case ExpressionStatement e:
                if (_isGeometry && TryEmitGeometryStmt(sb, e.Expression, pad))
                    break;
                sb.AppendLine($"{pad}{EmitExpression(e.Expression)};");
                break;

            case IfStatement i:
                sb.AppendLine($"{pad}if ({EmitExpression(i.Condition)})");
                EmitStatementOrBlock(sb, i.ThenBranch, indent);
                if (i.ElseBranch is not null)
                {
                    sb.AppendLine($"{pad}else");
                    EmitStatementOrBlock(sb, i.ElseBranch, indent);
                }
                break;

            case ForStatement f:
                sb.Append($"{pad}for (");
                if (f.Initializer is VariableDeclaration fv)
                {
                    // A `var` loop counter defaults to int, as in the HLSL generator.
                    string fSpec = fv.Type.Name == "var"
                        ? (fv.Initializer is not null ? _types.Infer(fv.Initializer) ?? "int" : "int")
                        : fv.Type.Name;
                    _types.Declare(fv.Name, fSpec);
                    string fInit = fv.Initializer is not null ? $" = {EmitExpression(fv.Initializer)}" : "";
                    sb.Append($"{GlslType(fSpec)} {EscapeId(fv.Name)}{fInit}");
                }
                else if (f.Initializer is ExpressionStatement fes)
                {
                    // Assignment to a counter declared earlier.
                    sb.Append(EmitExpression(fes.Expression));
                }
                sb.Append("; ");
                if (f.Condition is not null)
                    sb.Append(EmitExpression(f.Condition));
                sb.Append("; ");
                if (f.Increment is not null)
                    sb.Append(EmitExpression(f.Increment));
                sb.AppendLine(")");
                EmitStatementOrBlock(sb, f.Body, indent);
                break;

            case WhileStatement w:
                sb.AppendLine($"{pad}while ({EmitExpression(w.Condition)})");
                EmitStatementOrBlock(sb, w.Body, indent);
                break;

            case BlockStatement b:
                EmitBlock(sb, b, indent);
                break;

            case DiscardStatement:
                sb.AppendLine($"{pad}discard;");
                break;

            case BreakStatement:
                sb.AppendLine($"{pad}break;");
                break;

            case ContinueStatement:
                sb.AppendLine($"{pad}continue;");
                break;
        }
    }

    // Lowers `return expr;` in a stage body to output assignments plus a bare return.
    private void EmitStageReturn(StringBuilder sb, ReturnStatement ret, string pad)
    {
        if (ret.Value is not null)
        {
            if (_stageReturnStruct is not null)
            {
                string source = MaterializeReturnValue(sb, ret.Value, _stageReturnStruct, pad);
                foreach (var field in _stageReturnStruct.Fields)
                {
                    string fieldRef = $"{source}.{EscapeId(field.Name)}";
                    if (_returnMode == StageReturnMode.Vertex)
                    {
                        if (HasAttribute(field.Attributes, "Position"))
                            sb.AppendLine($"{pad}gl_Position = {fieldRef};");
                        else
                            sb.AppendLine($"{pad}v_{field.Name} = {fieldRef};");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}{EscapeId(field.Name)} = {fieldRef};");
                    }
                }
            }
            else if (_returnMode == StageReturnMode.Fragment)
            {
                sb.AppendLine($"{pad}fragColor = {EmitExpression(ret.Value)};");
            }
            else
            {
                // A bare vertex return is the clip-space position. The analyzer
                // only rejects void vertex returns, so this case is reachable.
                sb.AppendLine($"{pad}gl_Position = {EmitExpression(ret.Value)};");
            }
        }
        sb.AppendLine($"{pad}return;");
    }

    // An identifier is used as is. Any other returned expression goes into a
    // temporary first, so its fields can be read.
    private string MaterializeReturnValue(StringBuilder sb, Expression value, StructDeclaration returnStruct, string pad)
    {
        if (value is IdentifierExpression id)
            return EscapeId(id.Name);

        string temp = $"_ss_ret{_returnTempCounter++}";
        if (value is NewExpression ne && ne.Arguments.Count == 0)
            sb.AppendLine($"{pad}{GlslType(returnStruct.Name)} {temp};");
        else
            sb.AppendLine($"{pad}{GlslType(returnStruct.Name)} {temp} = {EmitExpression(value)};");
        return temp;
    }

    // Geometry bodies assign output fields by bare name: those become g_*
    // outputs, and the [Position] field becomes gl_Position.
    private bool TryEmitGeometryStmt(StringBuilder sb, Expression expr, string pad)
    {
        if (expr is AssignmentExpression a && a.Target is IdentifierExpression lhs
            && _geomOutputFieldNames is not null && _geomOutputFieldNames.Contains(lhs.Name))
        {
            string mapped = lhs.Name == _geomPositionField ? "gl_Position" : $"g_{lhs.Name}";
            sb.AppendLine($"{pad}{mapped} {MapOperator(a.Operator)} {EmitExpression(a.Value)};");
            return true;
        }
        return false;
    }

    private void EmitStatementOrBlock(StringBuilder sb, Statement stmt, int indent)
    {
        if (stmt is BlockStatement block)
        {
            EmitBlock(sb, block, indent);
            return;
        }

        // In a stage body one `return expr;` lowers to several statements. A
        // brace-less if/for/while would guard only the first, so wrap the body
        // in a block.
        if (_returnMode != StageReturnMode.None)
        {
            string pad = new(' ', indent * 4);
            sb.AppendLine($"{pad}{{");
            EmitStatement(sb, stmt, indent + 1);
            sb.AppendLine($"{pad}}}");
            return;
        }

        EmitStatement(sb, stmt, indent + 1);
    }

    private static readonly Dictionary<string, string> ComputeBuiltins = new(StringComparer.Ordinal)
    {
        ["GlobalInvocationID"] = "gl_GlobalInvocationID",
        ["LocalInvocationID"] = "gl_LocalInvocationID",
        ["WorkGroupID"] = "gl_WorkGroupID",
        ["LocalInvocationIndex"] = "gl_LocalInvocationIndex",
        ["NumWorkGroups"] = "gl_NumWorkGroups",
        ["WorkGroupSize"] = "gl_WorkGroupSize",
    };

    // Keep the decimal point: `1.0 / 3.0` emitted as `(1 / 3)` is integer
    // division in GLSL and evaluates to zero.
    private static string FormatFloat(float value)
    {
        string s = value.ToString("R", CultureInfo.InvariantCulture);
        if (!s.Contains('.') && !s.Contains('e') && !s.Contains('E')) s += ".0";
        return s;
    }

    private string EmitExpression(Expression expr)
    {
        switch (expr)
        {
            case IntLiteralExpression i:
                return i.Value.ToString();

            case FloatLiteralExpression f:
                return FormatFloat(f.Value);

            case BoolLiteralExpression b:
                return b.Value ? "true" : "false";

            case IdentifierExpression id:
                if (id.Name == "Position" && (_isVertex || _isGeometry))
                    return "gl_Position";
                if (_isCompute && ComputeBuiltins.TryGetValue(id.Name, out string? computeBuiltin))
                    return computeBuiltin;
                if (_isGeometry && id.Name == "PrimitiveID")
                    return "gl_PrimitiveIDIn";
                return EscapeId(id.Name);

            case BinaryExpression bin:
                return $"({EmitExpression(bin.Left)} {MapOperator(bin.Operator)} {EmitExpression(bin.Right)})";

            case UnaryExpression un:
                return $"({MapOperator(un.Operator)}{EmitExpression(un.Operand)})";

            case ConstructorExpression ctor:
                string ctorArgs = string.Join(", ", ctor.Arguments.Select(a => EmitExpression(a)));
                return $"{GlslType(ctor.Type.Name)}({ctorArgs})";

            case NewExpression newExpr:
                // GLSL has no 'new'.
                string newArgs = string.Join(", ", newExpr.Arguments.Select(a => EmitExpression(a)));
                return $"{newExpr.Type.Name}({newArgs})";

            case CallExpression call:
                return EmitCall(call);

            case MemberAccessExpression ma:
                return EmitMemberAccess(ma);

            case IndexExpression idx:
                return EmitIndexExpression(idx);

            case AssignmentExpression assign:
                string target = EmitExpression(assign.Target);
                string value = EmitExpression(assign.Value);
                return $"{target} {MapOperator(assign.Operator)} {value}";

            default:
                throw new NotSupportedException(
                    $"GLSL generator has no emission for expression node '{expr.GetType().Name}'.");
        }
    }

    private string EmitCall(CallExpression call)
    {
        if (call.Target is MemberAccessExpression ma && ma.Object is IdentifierExpression obj && obj.Name == "Math")
        {
            if (MathBuiltins.TryGetValue(ma.Member, out string? glslFunc))
            {
                string args = string.Join(", ", call.Arguments.Select(a => EmitExpression(a)));
                return $"{glslFunc}({args})";
            }
        }

        // tex.Sample(uv) → texture(tex, uv)
        if (call.Target is MemberAccessExpression sampleAccess && sampleAccess.Member == "Sample")
        {
            string texName = EmitExpression(sampleAccess.Object);
            string args = string.Join(", ", call.Arguments.Select(a => EmitExpression(a)));
            return $"texture({texName}, {args})";
        }

        // EmitVertex() and EndPrimitive() pass through as ordinary calls.
        if (call.Target is IdentifierExpression funcId)
        {
            if (funcId.Name == "Barrier" && _isCompute)
                return "barrier()";
            if (funcId.Name == "MemoryBarrier" && _isCompute)
                return "memoryBarrier()";
        }

        string callTarget = EmitExpression(call.Target);
        string callArgs = string.Join(", ", call.Arguments.Select(a => EmitExpression(a)));
        return $"{callTarget}({callArgs})";
    }

    private string EmitMemberAccess(MemberAccessExpression ma)
    {
        // input.field → a_field in the vertex stage, v_field or g_field in the fragment stage.
        if (!_isGeometry && _inputStruct is not null && _inputParam is not null
            && ma.Object is IdentifierExpression id && id.Name == _inputParam)
        {
            var field = _inputStruct.Fields.FirstOrDefault(f => f.Name == ma.Member);
            if (field is not null && !HasAttribute(field.Attributes, "Position"))
                return _isVertex ? $"a_{ma.Member}" : $"{_fragmentVaryingPrefix}{ma.Member}";
        }

        // vertices[i].field → v_field[i], or gl_in[i].gl_Position for the [Position] field.
        if (_isGeometry && _inputStruct is not null && _inputParam is not null
            && ma.Object is IndexExpression idx
            && idx.Object is IdentifierExpression arrayId && arrayId.Name == _inputParam)
        {
            string index = EmitExpression(idx.Index);
            var field = _inputStruct.Fields.FirstOrDefault(f => f.Name == ma.Member);
            if (field is not null && HasAttribute(field.Attributes, "Position"))
                return $"gl_in[{index}].gl_Position";
            return $"v_{ma.Member}[{index}]";
        }

        // Escaped to match the struct field declaration. Swizzle names are never reserved.
        return $"{EmitExpression(ma.Object)}.{EscapeId(ma.Member)}";
    }

    private string EmitIndexExpression(IndexExpression idx)
    {
        return $"{EmitExpression(idx.Object)}[{EmitExpression(idx.Index)}]";
    }

    // Raises the stage's #version so plain shaders stay on 330 core, the GL 3.3 baseline:
    //   400 double, 420 early_fragment_tests and depth_* qualifiers, 430 compute.
    private void Require(int version)
    {
        if (version > _minVersion)
            _minVersion = version;
    }

    private string FinishStage(StringBuilder body)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#version {_minVersion} core");
        sb.AppendLine();
        sb.Append(body);
        return sb.ToString();
    }

    private static StructDeclaration? FindStruct(string name, List<StructDeclaration> structs)
    {
        return structs.FirstOrDefault(s => s.Name == name);
    }

    private static AttributeSyntax? GetAttribute(IReadOnlyList<AttributeSyntax> attrs, string name)
    {
        return attrs.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAttribute(IReadOnlyList<AttributeSyntax> attrs, string name)
    {
        return attrs.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // Fallback when the attribute or argument is missing or not a literal.
    private static int GetIntArg(IReadOnlyList<AttributeSyntax> attrs, string name, int fallback, int argIndex = 0)
    {
        var attr = GetAttribute(attrs, name);
        if (attr is null || attr.Arguments.Count <= argIndex) return fallback;
        if (attr.Arguments[argIndex] is IntLiteralExpression i) return i.Value;
        return fallback;
    }

    private static string MapOperator(TokenKind kind) => kind switch
    {
        TokenKind.Plus => "+",
        TokenKind.Minus => "-",
        TokenKind.Star => "*",
        TokenKind.Slash => "/",
        TokenKind.Percent => "%",
        TokenKind.Equals => "==",
        TokenKind.NotEquals => "!=",
        TokenKind.Less => "<",
        TokenKind.LessEquals => "<=",
        TokenKind.Greater => ">",
        TokenKind.GreaterEquals => ">=",
        TokenKind.And => "&&",
        TokenKind.Or => "||",
        TokenKind.Not => "!",
        TokenKind.Assign => "=",
        TokenKind.PlusAssign => "+=",
        TokenKind.MinusAssign => "-=",
        TokenKind.StarAssign => "*=",
        TokenKind.SlashAssign => "/=",
        TokenKind.PercentAssign => "%=",
        _ => "?"
    };

    private string EmitArraySuffix(TypeSyntax type)
    {
        if (!type.IsArray)
            return "";
        if (type.ArraySize is not null)
            return $"[{EmitExpression(type.ArraySize)}]";
        return "[]";
    }

    private string GlslType(string name)
    {
        if (name == "double")
            Require(400);

        return name switch
        {
            "void" => "void",
            "bool" => "bool",
            "int" => "int",
            "uint" => "uint",
            "float" => "float",
            "double" => "double",
            "vec2" => "vec2",
            "vec3" => "vec3",
            "vec4" => "vec4",
            "ivec2" => "ivec2",
            "ivec3" => "ivec3",
            "ivec4" => "ivec4",
            "mat2" => "mat2",
            "mat3" => "mat3",
            "mat4" => "mat4",
            "sampler2D" => "sampler2D",
            "sampler2DArray" => "sampler2DArray",
            "sampler3D" => "sampler3D",
            "samplerCube" => "samplerCube",
            _ => name,
        };
    }
}
