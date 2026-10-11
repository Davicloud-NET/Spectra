using System;
using System.Collections.Generic;
using System.Text;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Writes a description of the script API as a definitions file for
/// luau-lsp, which gives a script's editor its completion and type checking.
/// The same description gives the same text. Nothing here needs Luau.
/// </summary>
// The syntax has no specification. Every construct is written in one place
// here, so a correction is one line.
public static class LuauDefinitions
{
    /// <summary>The name the file has in a project.</summary>
    public const string FileName = "spectra.d.luau";

    // The class a signal property is written as, with what it hands its
    // listeners as the type arguments.
    private const string SignalType = "RBXScriptSignal";

    private const string Indent = "    ";

    /// <summary>The whole file, with <c>\n</c> line ends.</summary>
    public static string Write(ScriptApiDescription api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var text = new StringBuilder();
        text.Append("-- The script API of Spectra Engine, for luau-lsp.\n");
        text.Append("-- Written by the engine from its own description. Do not edit.\n");

        foreach (ScriptClassDescription described in BasesFirst(api.Classes))
        {
            text.Append('\n');
            if (described.TypeParameters.Length > 0)
                WriteTableType(text, described);
            else
                WriteExternType(text, described);
        }

        foreach (ScriptLibraryDescription library in api.Libraries)
        {
            text.Append('\n');
            WriteLibrary(text, library);
        }

        foreach (ScriptPropertyDescription global in api.Globals)
        {
            text.Append('\n');
            WriteDoc(text, "", global.Summary, global.Deprecated);
            text.Append("declare ").Append(global.Name).Append(": ").Append(global.Type).Append('\n');
        }

        foreach (ScriptFunctionDescription function in api.Functions)
        {
            text.Append('\n');
            WriteGlobalFunction(text, function);
        }

        return text.ToString();
    }

    private static void WriteExternType(StringBuilder text, ScriptClassDescription described)
    {
        WriteDoc(text, "", described.Summary, "");
        text.Append("declare extern type ").Append(described.Name);
        if (described.Extends.Length > 0)
            text.Append(" extends ").Append(described.Extends);
        text.Append(" with\n");

        foreach (ScriptPropertyDescription property in described.Properties)
        {
            WriteDoc(text, Indent, property.Summary, property.Deprecated);
            text.Append(Indent).Append(property.Name).Append(": ").Append(property.Type).Append('\n');
        }

        foreach (ScriptSignalDescription signal in described.Signals)
        {
            WriteDoc(text, Indent, signal.Summary, "");
            text.Append(Indent).Append(signal.Name).Append(": ").Append(SignalOf(signal)).Append('\n');
        }

        foreach (ScriptFunctionDescription method in described.Methods)
        {
            WriteDoc(text, Indent, method.Summary, method.Deprecated);
            foreach (ScriptSignature signature in method.Signatures)
            {
                text.Append(Indent).Append("function ").Append(method.Name).Append("(self");
                AppendParameters(text, signature.Parameters, isType: false, hasSelf: true);
                text.Append("): ").Append(ReturnsOf(signature)).Append('\n');
            }
        }

        foreach (ScriptOperatorDescription op in described.Operators)
        {
            text.Append(Indent).Append("function ").Append(MetamethodOf(op.Operator)).Append("(self");
            if (op.Right.Length > 0)
                text.Append(", other: ").Append(op.Right);
            text.Append("): ").Append(op.Returns).Append('\n');
        }

        if (described.ChildType.Length > 0)
            text.Append(Indent).Append("[string]: ").Append(described.ChildType).Append('\n');

        text.Append("end\n");
    }

    // An extern type takes no type parameters, so a class with any is a
    // table type whose methods name the object as their first parameter.
    private static void WriteTableType(StringBuilder text, ScriptClassDescription described)
    {
        string self = $"{described.Name}<{WithoutDefaults(described.TypeParameters)}>";

        WriteDoc(text, "", described.Summary, "");
        text.Append("export type ").Append(described.Name).Append('<').Append(described.TypeParameters).Append("> = {\n");

        foreach (ScriptPropertyDescription property in described.Properties)
        {
            WriteDoc(text, Indent, property.Summary, property.Deprecated);
            text.Append(Indent).Append(property.Name).Append(": ").Append(property.Type).Append(",\n");
        }

        foreach (ScriptFunctionDescription method in described.Methods)
        {
            WriteDoc(text, Indent, method.Summary, method.Deprecated);
            text.Append(Indent).Append(method.Name).Append(": ");
            AppendFunctionType(text, method.Signatures, self);
            text.Append(",\n");
        }

        text.Append("}\n");
    }

    private static void WriteLibrary(StringBuilder text, ScriptLibraryDescription library)
    {
        WriteDoc(text, "", library.Summary, "");
        text.Append("declare ").Append(library.Name).Append(": {\n");

        foreach (ScriptPropertyDescription constant in library.Constants)
        {
            WriteDoc(text, Indent, constant.Summary, constant.Deprecated);
            text.Append(Indent).Append(constant.Name).Append(": ").Append(constant.Type).Append(",\n");
        }

        foreach (ScriptFunctionDescription function in library.Functions)
        {
            WriteDoc(text, Indent, function.Summary, function.Deprecated);
            text.Append(Indent).Append(function.Name).Append(": ");
            AppendFunctionType(text, function.Signatures, self: "");
            text.Append(",\n");
        }

        text.Append("}\n");
    }

    // A function with several shapes cannot be declared twice, so it is
    // declared as a value whose type joins them.
    private static void WriteGlobalFunction(StringBuilder text, ScriptFunctionDescription function)
    {
        WriteDoc(text, "", function.Summary, "");
        if (function.Deprecated.Length > 0)
            text.Append("@[deprecated {use = \"").Append(function.Deprecated).Append("\"}]\n");

        if (function.Signatures.Count != 1)
        {
            text.Append("declare ").Append(function.Name).Append(": ");
            AppendFunctionType(text, function.Signatures, self: "");
            text.Append('\n');
            return;
        }

        text.Append("declare function ").Append(function.Name).Append('(');
        AppendParameters(text, function.Signatures[0].Parameters, isType: false, hasSelf: false);
        text.Append("): ").Append(ReturnsOf(function.Signatures[0])).Append('\n');
    }

    // (a: A) -> R, or ((a: A) -> R) & ((b: B) -> R) for several shapes.
    private static void AppendFunctionType(StringBuilder text, IReadOnlyList<ScriptSignature> signatures, string self)
    {
        for (int i = 0; i < signatures.Count; i++)
        {
            if (i > 0)
                text.Append(" & ");
            if (signatures.Count > 1)
                text.Append('(');

            text.Append('(');
            if (self.Length > 0)
                text.Append("self: ").Append(self);
            AppendParameters(text, signatures[i].Parameters, isType: true, hasSelf: self.Length > 0);
            text.Append(") -> ").Append(ReturnsOf(signatures[i]));

            if (signatures.Count > 1)
                text.Append(')');
        }

        if (signatures.Count == 0)
            text.Append("() -> ()");
    }

    // In a declaration the rest of the arguments are "...: any". In a
    // function type they are "...any".
    private static void AppendParameters(
        StringBuilder text, IReadOnlyList<ScriptParameter> parameters, bool isType, bool hasSelf)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (i > 0 || hasSelf)
                text.Append(", ");

            ScriptParameter parameter = parameters[i];
            if (parameter.Name == "..." && isType)
                text.Append("...").Append(parameter.Type);
            else
                text.Append(parameter.Name).Append(": ").Append(parameter.Type);
        }
    }

    private static string ReturnsOf(ScriptSignature signature) =>
        signature.Returns.Length == 0 ? "()" : signature.Returns;

    private static string SignalOf(ScriptSignalDescription signal)
    {
        var type = new StringBuilder(SignalType).Append('<');
        for (int i = 0; i < signal.Parameters.Count; i++)
        {
            if (i > 0)
                type.Append(", ");
            type.Append(signal.Parameters[i].Type);
        }

        return type.Append('>').ToString();
    }

    // "T... = ...any, U" names its parameters as "T..., U".
    private static string WithoutDefaults(string typeParameters)
    {
        var names = new StringBuilder();
        foreach (string part in typeParameters.Split(','))
        {
            if (names.Length > 0)
                names.Append(", ");

            int assignment = part.IndexOf('=', StringComparison.Ordinal);
            names.Append((assignment < 0 ? part : part[..assignment]).Trim());
        }

        return names.ToString();
    }

    private static string MetamethodOf(string op) => op switch
    {
        "+" => "__add",
        "-" => "__sub",
        "*" => "__mul",
        "/" => "__div",
        "//" => "__idiv",
        "%" => "__mod",
        "^" => "__pow",
        ".." => "__concat",
        "==" => "__eq",
        "<" => "__lt",
        "<=" => "__le",
        "#" => "__len",
        "unm" => "__unm",
        _ => throw new ArgumentException($"'{op}' is no operator a definitions file can declare.", nameof(op)),
    };

    private static void WriteDoc(StringBuilder text, string indent, string summary, string deprecated)
    {
        if (summary.Length > 0)
            text.Append(indent).Append("--- ").Append(summary).Append('\n');
        if (deprecated.Length > 0)
            text.Append(indent).Append("--- Deprecated: use ").Append(deprecated).Append(".\n");
    }

    // A class after the one it extends. The order is otherwise the description's.
    private static List<ScriptClassDescription> BasesFirst(IReadOnlyList<ScriptClassDescription> classes)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (ScriptClassDescription described in classes)
            declared.Add(described.Name);

        var ordered = new List<ScriptClassDescription>(classes.Count);
        var written = new HashSet<ScriptClassDescription>(ReferenceEqualityComparer.Instance);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int pass = 0; pass < classes.Count && ordered.Count < classes.Count; pass++)
        {
            foreach (ScriptClassDescription described in classes)
            {
                bool isBaseWritten = described.Extends.Length == 0
                    || names.Contains(described.Extends)
                    || !declared.Contains(described.Extends);
                if (isBaseWritten && written.Add(described))
                {
                    ordered.Add(described);
                    names.Add(described.Name);
                }
            }
        }

        // What is left extends itself in a circle. It is written as it came.
        foreach (ScriptClassDescription described in classes)
        {
            if (written.Add(described))
                ordered.Add(described);
        }

        return ordered;
    }
}
