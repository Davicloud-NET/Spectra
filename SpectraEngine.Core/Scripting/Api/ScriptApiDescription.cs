using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Everything a script can reach, as data. The binding is generated from
/// the same declarations, and the definitions file and the reference are
/// written from this.
/// </summary>
public sealed record ScriptApiDescription
{
    /// <summary>The classes and the value types, in the order of their names.</summary>
    public IReadOnlyList<ScriptClassDescription> Classes { get; init; } = [];

    /// <summary>The global tables of functions and constants, such as <c>task</c>.</summary>
    public IReadOnlyList<ScriptLibraryDescription> Libraries { get; init; } = [];

    /// <summary>The global values, such as <c>game</c>.</summary>
    public IReadOnlyList<ScriptPropertyDescription> Globals { get; init; } = [];

    /// <summary>The global functions, such as <c>wait</c>.</summary>
    public IReadOnlyList<ScriptFunctionDescription> Functions { get; init; } = [];

    /// <summary>
    /// The entity classes a level can use, in the order of their names: each
    /// input a method and each output a signal. Empty until
    /// <see cref="WithEntityClasses"/> has read them from schemas, since a
    /// build's own description knows no level.
    /// </summary>
    public IReadOnlyList<ScriptClassDescription> EntityClasses { get; init; } = [];

    /// <summary>
    /// This description with the classes of the schemas as its
    /// <see cref="EntityClasses"/>. The definitions file and the reference
    /// then have them.
    /// </summary>
    public ScriptApiDescription WithEntityClasses(IEnumerable<Entities.EntitySchema> schemas) =>
        this with { EntityClasses = EntityClassApi.Describe(schemas, this) };

    /// <summary>The class or value type of that name, or null.</summary>
    public ScriptClassDescription? FindClass(string name)
    {
        foreach (ScriptClassDescription described in Classes)
        {
            if (string.Equals(described.Name, name, StringComparison.Ordinal))
                return described;
        }

        return null;
    }

    /// <summary>
    /// The names of every property, method and signal an object of the class
    /// has, with those of the classes it extends.
    /// </summary>
    public IReadOnlyCollection<string> MemberNamesOf(string className)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);

        // The depth is bounded, so a class that extends itself ends.
        ScriptClassDescription? described = FindClass(className);
        for (int depth = 0; described is not null && depth < 32; depth++)
        {
            foreach (ScriptPropertyDescription property in described.Properties)
                names.Add(property.Name);
            foreach (ScriptFunctionDescription method in described.Methods)
                names.Add(method.Name);
            foreach (ScriptSignalDescription signal in described.Signals)
                names.Add(signal.Name);

            described = described.Extends.Length == 0 ? null : FindClass(described.Extends);
        }

        return names;
    }

    /// <summary>
    /// The class a child read by its name is of, for an object of the class:
    /// what the class says, or the nearest class it extends that says one.
    /// Empty when none does.
    /// </summary>
    public string ChildTypeOf(string className)
    {
        ScriptClassDescription? described = FindClass(className);
        for (int depth = 0; described is not null && depth < 32; depth++)
        {
            if (described.ChildType.Length > 0)
                return described.ChildType;

            described = described.Extends.Length == 0 ? null : FindClass(described.Extends);
        }

        return "";
    }
}
