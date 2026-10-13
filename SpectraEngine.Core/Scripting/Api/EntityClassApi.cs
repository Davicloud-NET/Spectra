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

    // What a schema does not say may be anything, or nothing.
    private static readonly ScriptParameter[] Anything = [new("...", "any")];

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
    /// What a script hands an input of that parameter, and what an output of
    /// it hands a listener: each value with a name and its type as Luau
    /// writes it. A parameter that does not say is any values at all.
    /// </summary>
    public static IReadOnlyList<ScriptParameter> ValuesOf(ParameterType parameter)
    {
        if (!parameter.IsSaid)
            return Anything;
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
        ArgumentNullException.ThrowIfNull(api);

        string extends = ExtendsOf(schema);
        var taken = new HashSet<string>(api.MemberNamesOf(extends), StringComparer.Ordinal);

        return new EntityClassMembers(
            schema.ClassName, extends, Free(schema.Inputs, taken), Free(schema.Outputs, taken));
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
            if (IsLuauName(name) && !LuauTypes.Contains(name) && api.FindClass(name) is null)
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
            signals.Add(new ScriptSignalDescription { Name = output.Name, Parameters = ValuesOf(output.Parameter) });

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
