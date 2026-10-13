using SpectraEngine.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Writes a description of the script API as canonical JSON, which the docs
/// site makes the script reference from. The same description gives the
/// same bytes.
/// </summary>
public static class ScriptApiJson
{
    /// <summary>The shape of the file. Raised when a field is renamed, removed or changes its meaning.</summary>
    public const int Format = 1;

    /// <summary>The whole description as UTF-8.</summary>
    public static byte[] Write(ScriptApiDescription api)
    {
        ArgumentNullException.ThrowIfNull(api);

        return CanonicalJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", Format);
            WriteArray(writer, "classes", api.Classes, WriteClass);
            WriteArray(writer, "libraries", api.Libraries, WriteLibrary);
            WriteArray(writer, "globals", api.Globals, WriteProperty);
            WriteArray(writer, "functions", api.Functions, WriteFunction);
            WriteArray(writer, "entityClasses", api.EntityClasses, WriteClass);
            writer.WriteEndObject();
        });
    }

    private static void WriteClass(Utf8JsonWriter writer, ScriptClassDescription described)
    {
        writer.WriteString("name", described.Name);
        writer.WriteString("extends", described.Extends);
        writer.WriteBoolean("isValue", described.IsValue);
        writer.WriteString("typeParameters", described.TypeParameters);
        writer.WriteString("childType", described.ChildType);
        writer.WriteString("summary", described.Summary);
        WriteArray(writer, "properties", described.Properties, WriteProperty);
        WriteArray(writer, "methods", described.Methods, WriteFunction);
        WriteArray(writer, "signals", described.Signals, WriteSignal);
        WriteArray(writer, "operators", described.Operators, WriteOperator);
    }

    private static void WriteLibrary(Utf8JsonWriter writer, ScriptLibraryDescription library)
    {
        writer.WriteString("name", library.Name);
        writer.WriteString("summary", library.Summary);
        WriteArray(writer, "constants", library.Constants, WriteProperty);
        WriteArray(writer, "functions", library.Functions, WriteFunction);
    }

    private static void WriteProperty(Utf8JsonWriter writer, ScriptPropertyDescription property)
    {
        writer.WriteString("name", property.Name);
        writer.WriteString("type", property.Type);
        writer.WriteString("summary", property.Summary);
        writer.WriteBoolean("canSet", property.CanSet);
        writer.WriteString("deprecated", property.Deprecated);
    }

    private static void WriteFunction(Utf8JsonWriter writer, ScriptFunctionDescription function)
    {
        writer.WriteString("name", function.Name);
        writer.WriteString("summary", function.Summary);
        writer.WriteString("deprecated", function.Deprecated);
        WriteArray(writer, "signatures", function.Signatures, static (writer, signature) =>
        {
            WriteArray(writer, "parameters", signature.Parameters, WriteParameter);
            writer.WriteString("returns", signature.Returns);
        });
    }

    private static void WriteSignal(Utf8JsonWriter writer, ScriptSignalDescription signal)
    {
        writer.WriteString("name", signal.Name);
        writer.WriteString("summary", signal.Summary);
        WriteArray(writer, "parameters", signal.Parameters, WriteParameter);
    }

    private static void WriteOperator(Utf8JsonWriter writer, ScriptOperatorDescription op)
    {
        writer.WriteString("operator", op.Operator);
        writer.WriteString("right", op.Right);
        writer.WriteString("returns", op.Returns);
    }

    private static void WriteParameter(Utf8JsonWriter writer, ScriptParameter parameter)
    {
        writer.WriteString("name", parameter.Name);
        writer.WriteString("type", parameter.Type);
    }

    private static void WriteArray<T>(
        Utf8JsonWriter writer, string name, IReadOnlyList<T> items, Action<Utf8JsonWriter, T> write)
    {
        writer.WriteStartArray(name);
        foreach (T item in items)
        {
            writer.WriteStartObject();
            write(writer, item);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
