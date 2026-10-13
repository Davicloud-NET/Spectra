using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// What an entity class is to a script: an input is a method and an output
/// is a signal. A class is data, so this reads its schema where the rest of
/// the script API reads attributes. The binding answers a script by the same
/// members the description lists.
/// </summary>
public static class EntityClassApi
{
    private static readonly ScriptParameter[] Nothing = [];

    // What a schema does not say: a script hands over one value a wire can
    // carry or none, and a listener gets the wire's text or nothing.
    private static readonly ScriptParameter[] OneOfAny = [new("value", "(boolean | number | string | Vector3 | Instance)?")];
    private static readonly ScriptParameter[] TextOrNothing = [new("text", "string?")];

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    };

    // A class of one of these names would take the place of Luau's own type.
    private static readonly HashSet<string> LuauTypes = new(StringComparer.Ordinal)
    {
        "any", "boolean", "buffer", "never", "number", "string", "thread", "unknown", "vector",
    };

    /// <summary>
    /// The script class a node of the entity class is of besides: Part for a
    /// class placed as a brush, and Instance for any other.
    /// </summary>
    public static string ExtendsOf(EntitySchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return schema.Placement is EntityPlacement.Brush or EntityPlacement.Volume
            ? NodeClassName.Part
            : NodeClass.Instance;
    }

    /// <summary>What a value of that type is to a script.</summary>
    /// <exception cref="ArgumentException">No input or output carries a value of that type.</exception>
    public static PortValueKind KindOf(KeyvalueType type) => type switch
    {
        KeyvalueType.Bool => PortValueKind.Boolean,
        KeyvalueType.Int => PortValueKind.WholeNumber,
        KeyvalueType.Float => PortValueKind.Number,
        KeyvalueType.String or KeyvalueType.TargetName => PortValueKind.Text,
        KeyvalueType.Vec2 => PortValueKind.TwoNumbers,
        KeyvalueType.Vec3 or KeyvalueType.Color or KeyvalueType.Angles => PortValueKind.Vector3,
        KeyvalueType.Vec4 => PortValueKind.FourNumbers,
        KeyvalueType.NodeRef => PortValueKind.Node,
        _ => throw new ArgumentException($"No input or output carries a {type}.", nameof(type)),
    };

    /// <summary>
    /// What a script hands an input of that parameter: each value with a
    /// name and its type as Luau writes it. A parameter that does not say
    /// takes one value of a kind a wire can carry, or none.
    /// </summary>
    public static IReadOnlyList<ScriptParameter> ValuesOf(ParameterType parameter)
    {
        if (!parameter.IsSaid)
            return OneOfAny;
        if (!parameter.HasValue)
            return Nothing;

        string optional = parameter.IsOptional ? "?" : "";
        return KindOf(parameter.ValueType) switch
        {
            PortValueKind.Boolean => [new("value", "boolean" + optional)],
            PortValueKind.Number or PortValueKind.WholeNumber => [new("value", "number" + optional)],
            PortValueKind.Text => [new(parameter.ValueType == KeyvalueType.TargetName ? "name" : "value", "string" + optional)],
            PortValueKind.Vector3 => [new("value", "Vector3" + optional)],
            PortValueKind.TwoNumbers => Numbers(["x", "y"], optional),
            PortValueKind.FourNumbers => Numbers(["x", "y", "z", "w"], optional),

            // No node is a value of its own, so the type says nil either way.
            _ => [new("node", "Instance?")],
        };
    }

    /// <summary>
    /// What an output of that parameter hands a listener. The same as
    /// <see cref="ValuesOf"/>, but for a parameter that does not say: the
    /// listener then gets the wire's text, or nothing when it is empty.
    /// </summary>
    public static IReadOnlyList<ScriptParameter> SentValuesOf(ParameterType parameter) =>
        parameter.IsSaid ? ValuesOf(parameter) : TextOrNothing;

    /// <summary>
    /// The inputs and outputs of a class that a script reaches as members. A
    /// name stays out when Luau cannot write it, and when a member of the
    /// class it extends has it: that member wins. An input wins over an
    /// output of its name.
    /// </summary>
    /// <param name="schema">The class.</param>
    /// <param name="api">The script API the class joins, for the names its members may not take.</param>
    public static EntityClassMembers MembersOf(EntitySchema schema, ScriptApiDescription api)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return MembersOf(schema, api, ExtendsOf(schema));
    }

    /// <summary>
    /// The same on a node of the script class <paramref name="carrier"/>,
    /// whose members are the ones that win. The binding asks by what the
    /// node carries, since a class placed as a brush may sit on a node that
    /// is no part, and a point class on a light.
    /// </summary>
    public static EntityClassMembers MembersOf(EntitySchema schema, ScriptApiDescription api, string carrier)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(carrier);

        var taken = new HashSet<string>(api.MemberNamesOf(carrier), StringComparer.Ordinal);

        return new EntityClassMembers(
            schema.ClassName, carrier, Free(schema.Inputs, taken), Free(schema.Outputs, taken));
    }

    /// <summary>
    /// The classes as script classes, in the order of their names. A class
    /// stays out when Luau cannot write its name as a type, and when the
    /// script API has a class of that name already.
    /// </summary>
    public static IReadOnlyList<ScriptClassDescription> Describe(IEnumerable<EntitySchema> schemas, ScriptApiDescription api)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(api);

        var described = new SortedDictionary<string, ScriptClassDescription>(StringComparer.Ordinal);
        foreach (EntitySchema schema in schemas)
        {
            string name = schema.ClassName;
            if (WhyNoClass(name, api) is null)
                described.TryAdd(name, Describe(schema, MembersOf(schema, api)));
        }

        return [.. described.Values];
    }

    /// <summary>Whether Luau can write the name after a dot or a colon.</summary>
    public static bool IsLuauName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0 || char.IsAsciiDigit(name[0]) || Reserved.Contains(name))
            return false;

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }

    // Why the description has no class of that name: the end of a sentence.
    // Null when it has one.
    internal static string? WhyNoClass(string name, ScriptApiDescription api)
    {
        if (!IsLuauName(name))
            return "Luau cannot write its name as a type";
        if (LuauTypes.Contains(name))
            return "Luau has a type of that name";

        return api.FindClass(name) is null ? null : "the script API has a class of that name";
    }

    private static ScriptClassDescription Describe(EntitySchema schema, EntityClassMembers members)
    {
        var methods = new List<ScriptFunctionDescription>(members.Inputs.Count);
        foreach (EntityPort input in members.Inputs)
        {
            methods.Add(new ScriptFunctionDescription
            {
                Name = input.Name,
                Signatures = [new ScriptSignature(ValuesOf(input.Parameter), "")],
            });
        }

        var signals = new List<ScriptSignalDescription>(members.Outputs.Count);
        foreach (EntityPort output in members.Outputs)
            signals.Add(new ScriptSignalDescription { Name = output.Name, Parameters = SentValuesOf(output.Parameter) });

        return new ScriptClassDescription
        {
            Name = schema.ClassName,
            Extends = members.Extends,
            Summary = SummaryOf(schema),
            Methods = methods,
            Signals = signals,
        };
    }

    // A schema has a label and a group for its class, and no words for an
    // input or an output.
    private static string SummaryOf(EntitySchema schema)
    {
        string label = schema.DisplayName.Length > 0 ? schema.DisplayName : schema.ClassName;
        return schema.Group.Length > 0
            ? $"{label}. An entity class of the group {schema.Group}."
            : $"{label}. An entity class.";
    }

    private static List<EntityPort> Free(IReadOnlyList<EntityPort> ports, HashSet<string> taken)
    {
        var free = new List<EntityPort>(ports.Count);
        foreach (EntityPort port in ports)
        {
            // Adding takes the name, so an output of an input's name stays out.
            if (IsLuauName(port.Name) && taken.Add(port.Name))
                free.Add(port);
        }

        return free;
    }

    private static ScriptParameter[] Numbers(string[] names, string optional)
    {
        var numbers = new ScriptParameter[names.Length];
        for (int i = 0; i < names.Length; i++)
            numbers[i] = new ScriptParameter(names[i], "number" + optional);

        return numbers;
    }
}
