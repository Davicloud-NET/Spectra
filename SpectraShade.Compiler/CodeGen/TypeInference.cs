using SpectraShade.Compiler.Syntax;

namespace SpectraShade.Compiler.CodeGen;

// Expression type inference shared by the code generators, in SpectraShade
// type names ("vec3", "mat4"). Stopgap until the analyzer produces a typed AST:
// covers the common shapes and returns null when it cannot tell.
internal sealed class TypeInference
{
    private readonly Dictionary<string, string> _env = new();
    private IReadOnlyList<StructDeclaration> _structs = [];
    private IReadOnlyList<FunctionDeclaration> _helpers = [];

    public void Configure(IReadOnlyList<StructDeclaration> structs, IReadOnlyList<FunctionDeclaration> helpers)
    {
        _structs = structs;
        _helpers = helpers;
    }

    public void Declare(string name, string typeName) => _env[name] = typeName;

    public void DeclareGlobals(IEnumerable<CBufferDeclaration> cbuffers, IEnumerable<SamplerDeclaration> samplers)
    {
        foreach (var cb in cbuffers)
            foreach (var f in cb.Fields)
                _env[f.Name] = f.Type.Name;
        foreach (var s in samplers)
            _env[s.Name] = s.Type.Name;
    }

    public void Reset() => _env.Clear();

    public Dictionary<string, string> Snapshot() => new(_env);

    public void Restore(Dictionary<string, string> snapshot)
    {
        _env.Clear();
        foreach (var kv in snapshot)
            _env[kv.Key] = kv.Value;
    }

    public string? Infer(Expression expr)
    {
        switch (expr)
        {
            case IdentifierExpression id:
                return _env.TryGetValue(id.Name, out var t) ? t : null;

            case ConstructorExpression c:
                return c.Type.Name;

            case NewExpression n:
                return n.Type.Name;

            case IntLiteralExpression:
                return "int";

            case FloatLiteralExpression:
                return "float";

            case BoolLiteralExpression:
                return "bool";

            case MemberAccessExpression ma:
                return InferMember(ma);

            case BinaryExpression b:
            {
                var lt = Infer(b.Left);
                var rt = Infer(b.Right);
                if (IsMatrixType(lt) && IsVectorType(rt)) return rt;
                if (IsVectorType(lt) && IsMatrixType(rt)) return lt;
                return lt ?? rt;
            }

            case CallExpression call:
                return InferCall(call);

            case IndexExpression ix:
                return Infer(ix.Object);

            case UnaryExpression un:
                return Infer(un.Operand);

            case AssignmentExpression a:
                return Infer(a.Target);
        }
        return null;
    }

    private string? InferMember(MemberAccessExpression ma)
    {
        var objType = Infer(ma.Object);
        if (objType is null) return null;

        if (IsVectorType(objType))
        {
            int dim = ma.Member.Length;
            string scalar = VectorScalar(objType);
            return dim switch
            {
                1 => scalar,
                2 => ScalarToVec(scalar, 2),
                3 => ScalarToVec(scalar, 3),
                4 => ScalarToVec(scalar, 4),
                _ => null,
            };
        }

        var declaration = _structs.FirstOrDefault(sd => sd.Name == objType);
        if (declaration is not null)
        {
            var field = declaration.Fields.FirstOrDefault(f => f.Name == ma.Member);
            return field?.Type.Name;
        }
        return null;
    }

    private string? InferCall(CallExpression call)
    {
        if (call.Target is MemberAccessExpression ma
            && ma.Object is IdentifierExpression o && o.Name == "Math")
        {
            // Scalar-returning builtins; everything else follows its first argument.
            return ma.Member.ToLowerInvariant() switch
            {
                "dot" or "length" or "distance" or "determinant" => "float",
                "cross" => "vec3",
                _ => call.Arguments.Count > 0 ? Infer(call.Arguments[0]) : null,
            };
        }
        if (call.Target is MemberAccessExpression sa && sa.Member == "Sample")
            return "vec4";
        if (call.Target is IdentifierExpression fid)
        {
            var fn = _helpers.FirstOrDefault(f => f.Name == fid.Name);
            return fn?.ReturnType.Name;
        }
        return null;
    }

    public static bool IsMatrixType(string? t) => t is "mat2" or "mat3" or "mat4"
        or "float2x2" or "float3x3" or "float4x4";

    public static bool IsVectorType(string? t) => t is
        "vec2" or "vec3" or "vec4"
        or "ivec2" or "ivec3" or "ivec4"
        or "uvec2" or "uvec3" or "uvec4"
        or "bvec2" or "bvec3" or "bvec4";

    public static string VectorScalar(string t) => t[0] switch
    {
        'i' => "int",
        'u' => "uint",
        'b' => "bool",
        _ => "float",
    };

    public static string ScalarToVec(string scalar, int dim)
        => scalar switch
        {
            "int" => $"ivec{dim}",
            "uint" => $"uvec{dim}",
            "bool" => $"bvec{dim}",
            _ => $"vec{dim}",
        };
}
