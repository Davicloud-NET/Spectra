using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler.Lexing;
using SpectraShade.Compiler.Syntax;
using System.Diagnostics.CodeAnalysis;

namespace SpectraShade.Compiler.Analysis;

/// <summary>
/// Builds the instanced twin of a shader: a <c>[PerInstance]</c> cbuffer field
/// becomes a per-instance vertex input, and the original shader is untouched.
/// </summary>
// Three edits, no expression rewriting: the field leaves its cbuffer, a field
// of the same name joins the vertex input struct, and the vertex body gets a
// leading local (mat4 uModel = input.uModel;) so existing references still
// resolve. Neither code generator has to know.
public static class InstancedVariant
{
    /// <summary>Marks a <c>cbuffer</c> field that may also arrive per instance.</summary>
    public const string Attribute = VertexInputLayout.PerInstanceAttribute;

    /// <summary>The only type a per-instance uniform may have.</summary>
    public const string SupportedType = "mat4";

    /// <summary>
    /// Builds the instanced twin of <paramref name="unit"/>. Returns false if it
    /// declares no per-instance uniform.
    /// </summary>
    public static bool TryBuild(CompilationUnit unit, [NotNullWhen(true)] out CompilationUnit? instanced)
    {
        instanced = null;
        if (unit is null)
            return false;

        FieldDeclaration? marked = null;
        CBufferDeclaration? owner = null;
        foreach (SyntaxNode member in unit.Shader.Members)
        {
            if (member is not CBufferDeclaration cbuffer)
                continue;

            foreach (FieldDeclaration field in cbuffer.Fields)
            {
                if (!VertexInputLayout.IsPerInstance(field))
                    continue;

                // First one wins. The analyzer rejects a second.
                marked = field;
                owner = cbuffer;
                break;
            }

            if (marked is not null)
                break;
        }

        if (marked is null || owner is null)
            return false;
        if (marked.Type.Name != SupportedType)
            return false;

        FunctionDeclaration? vertex = null;
        foreach (SyntaxNode member in unit.Shader.Members)
        {
            if (member is FunctionDeclaration f && f.HasAttribute("Vertex"))
            {
                vertex = f;
                break;
            }
        }

        if (vertex is null || vertex.Parameters.Count == 0)
            return false;

        var allStructs = new List<StructDeclaration>(unit.Structs);
        foreach (SyntaxNode member in unit.Shader.Members)
        {
            if (member is StructDeclaration s)
                allStructs.Add(s);
        }

        string inputTypeName = vertex.Parameters[0].Type.Name;
        StructDeclaration? inputStruct = allStructs.FirstOrDefault(s => s.Name == inputTypeName);
        if (inputStruct is null)
            return false;

        StructDeclaration rewrittenInput = WithInstanceField(inputStruct, marked);
        CBufferDeclaration rewrittenCbuffer = WithoutField(owner, marked);
        FunctionDeclaration rewrittenVertex = WithLeadingLocal(vertex, marked, vertex.Parameters[0].Name);

        instanced = Replace(unit, inputStruct, rewrittenInput, owner, rewrittenCbuffer, vertex, rewrittenVertex);
        return true;
    }

    /// <summary>
    /// The first location past everything <paramref name="inputStruct"/> already
    /// claims, counting multi-location types.
    /// </summary>
    public static uint NextFreeLocation(StructDeclaration inputStruct)
    {
        uint next = 0;
        for (int i = 0; i < inputStruct.Fields.Count; i++)
        {
            FieldDeclaration field = inputStruct.Fields[i];
            if (!VertexInputLayout.TryDescribeType(field.Type.Name, out _, out uint span))
                continue;

            uint end = VertexInputLayout.ResolveLocation(field, i) + span;
            if (end > next)
                next = end;
        }
        return next;
    }

    private static StructDeclaration WithInstanceField(StructDeclaration inputStruct, FieldDeclaration marked)
    {
        SourceSpan span = marked.Span;
        uint location = NextFreeLocation(inputStruct);

        var attributes = new List<AttributeSyntax>
        {
            new("Location", [new IntLiteralExpression((int)location, span)], span),
            new(VertexInputLayout.PerInstanceAttribute, [], span),
        };

        // Same name as the uniform, so the leading local can stand in for it.
        var field = new FieldDeclaration(attributes, marked.Type, marked.Name, span);

        var fields = new List<FieldDeclaration>(inputStruct.Fields) { field };
        return new StructDeclaration(inputStruct.Name, fields, inputStruct.Span);
    }

    private static CBufferDeclaration WithoutField(CBufferDeclaration cbuffer, FieldDeclaration marked)
    {
        var fields = new List<FieldDeclaration>(cbuffer.Fields.Count);
        foreach (FieldDeclaration field in cbuffer.Fields)
        {
            if (!ReferenceEquals(field, marked))
                fields.Add(field);
        }

        return new CBufferDeclaration(cbuffer.Attributes, cbuffer.Name, fields, cbuffer.Span);
    }

    private static FunctionDeclaration WithLeadingLocal(
        FunctionDeclaration vertex, FieldDeclaration marked, string inputParameterName)
    {
        SourceSpan span = marked.Span;

        // mat4 <name> = <input>.<name>;
        var initializer = new MemberAccessExpression(
            new IdentifierExpression(inputParameterName, span), marked.Name, span);
        var local = new VariableDeclaration(marked.Type, marked.Name, initializer, span);

        var statements = new List<SyntaxNode>(vertex.Body.Statements.Count + 1) { local };
        statements.AddRange(vertex.Body.Statements);

        var body = new BlockStatement(statements, vertex.Body.Span);
        return new FunctionDeclaration(
            vertex.Attributes, vertex.ReturnType, vertex.Name, vertex.Parameters, body, vertex.Span);
    }

    // Everything else is shared by reference; syntax nodes are immutable.
    private static CompilationUnit Replace(
        CompilationUnit unit,
        StructDeclaration oldInput, StructDeclaration newInput,
        CBufferDeclaration oldCbuffer, CBufferDeclaration newCbuffer,
        FunctionDeclaration oldVertex, FunctionDeclaration newVertex)
    {
        var structs = new List<StructDeclaration>(unit.Structs.Count);
        foreach (StructDeclaration s in unit.Structs)
            structs.Add(ReferenceEquals(s, oldInput) ? newInput : s);

        var members = new List<SyntaxNode>(unit.Shader.Members.Count);
        foreach (SyntaxNode member in unit.Shader.Members)
        {
            members.Add(member switch
            {
                _ when ReferenceEquals(member, oldInput) => newInput,
                _ when ReferenceEquals(member, oldCbuffer) => newCbuffer,
                _ when ReferenceEquals(member, oldVertex) => newVertex,
                _ => member,
            });
        }

        var shader = new ShaderDeclaration(unit.Shader.Name, members, unit.Shader.Span);
        return new CompilationUnit(unit.Imports, structs, shader, unit.Span);
    }
}
