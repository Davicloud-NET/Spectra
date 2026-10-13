using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// What of an entity class a script cannot reach, as sentences for a log.
/// <see cref="EntityClassApi"/> leaves such a class or member out and says
/// nothing, so whoever writes the definitions file or runs a level asks here.
/// </summary>
public static class EntityClassGaps
{
    /// <summary>
    /// One sentence for each class the description of the schemas has not
    /// got, and for each input and output it leaves out of a class it has.
    /// </summary>
    public static IReadOnlyList<string> Of(IEnumerable<EntitySchema> schemas, ScriptApiDescription api)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(api);

        var gaps = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (EntitySchema schema in schemas)
        {
            // A class listed twice is described once, and has one set of gaps.
            if (!seen.Add(schema.ClassName))
                continue;

            if (EntityClassApi.WhyNoClass(schema.ClassName, api) is { } why)
                gaps.Add($"The entity class {schema.ClassName} is not in the definitions file: {why}.");
            else
                gaps.AddRange(OfPorts(schema, api, EntityClassApi.ExtendsOf(schema)));
        }

        return gaps;
    }

    /// <summary>
    /// One sentence for each input and output of the class that is no member
    /// of a node of the script class <paramref name="carrier"/>, with why.
    /// </summary>
    public static IReadOnlyList<string> OfPorts(EntitySchema schema, ScriptApiDescription api, string carrier)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(carrier);

        // Most classes have none, and then nothing more is looked up.
        EntityClassMembers members = EntityClassApi.MembersOf(schema, api, carrier);
        if (members.Inputs.Count == schema.Inputs.Count && members.Outputs.Count == schema.Outputs.Count)
            return [];

        IReadOnlyCollection<string> ofCarrier = api.MemberNamesOf(carrier);
        var gaps = new List<string>();
        foreach (EntityPort input in schema.Inputs)
        {
            if (WhyNoMember(input.Name, carrier, ofCarrier) is { } why)
                gaps.Add($"No script reaches the input {input.Name} of {schema.ClassName}: {why}.");
        }

        foreach (EntityPort output in schema.Outputs)
        {
            string? why = WhyNoMember(output.Name, carrier, ofCarrier)
                ?? (members.TryGetInput(output.Name, out _) ? "the input of that name wins" : null);
            if (why is not null)
                gaps.Add($"No script reaches the output {output.Name} of {schema.ClassName}: {why}.");
        }

        return gaps;
    }

    // The two rules that hold for an input and an output alike, in the order
    // EntityClassApi applies them. Null when neither keeps the name out.
    private static string? WhyNoMember(string name, string carrier, IReadOnlyCollection<string> ofCarrier)
    {
        if (!EntityClassApi.IsLuauName(name))
            return "Luau cannot write the name after a dot or a colon";

        foreach (string taken in ofCarrier)
        {
            if (string.Equals(taken, name, StringComparison.Ordinal))
                return $"{carrier} has a member of that name, which wins";
        }

        return null;
    }
}
