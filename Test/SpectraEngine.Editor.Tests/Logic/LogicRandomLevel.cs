using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Tests.Logic;

// Levels made from a seed, with everything wiring can do wrong in them:
// cycles, wires to self, shared names, prefixes, missing targets, unknown
// classes and long labels. The same seed always makes the same level.
internal static class LogicRandomLevel
{
    private static readonly string[] Classes =
    [
        "func_button", "func_door", "func_movelinear", "logic_relay",
        "math_counter", "trigger_multiple", "logic_timer", "mystery_box",
    ];

    public static LogicEntityInfo[] Entities(int seed)
    {
        var random = new Random(seed);
        int count = random.Next(2, 61);

        var names = new string[count];
        var classes = new string[count];

        for (int i = 0; i < count; i++)
        {
            // Now and then two entities share a name, and then both receive.
            names[i] = i > 0 && random.Next(12) == 0 ? names[random.Next(i)] : $"e{i}";
            classes[i] = Classes[random.Next(Classes.Length)];
        }

        var entities = new LogicEntityInfo[count];
        for (int i = 0; i < count; i++)
        {
            // The first entity always sends, so no level comes out with no wires.
            var wires = new EntityConnection[random.Next(i == 0 ? 1 : 0, 4)];
            for (int wire = 0; wire < wires.Length; wire++)
                wires[wire] = Wire(random, names, classes, i);

            entities[i] = new LogicEntityInfo(LogicFixture.Id(i + 1), names[i], classes[i], wires);
        }

        return entities;
    }

    // The same entities in another order, as a scene might list them after a reload.
    public static LogicEntityInfo[] Shuffled(LogicEntityInfo[] entities, int seed)
    {
        var random = new Random(seed);
        LogicEntityInfo[] shuffled = [.. entities];
        random.Shuffle(shuffled);
        return shuffled;
    }

    private static EntityConnection Wire(Random random, string[] names, string[] classes, int sender)
    {
        int receiver = random.Next(names.Length);
        int kind = random.Next(100);

        string target = kind switch
        {
            < 62 => names[receiver],
            < 70 => names[sender],
            < 75 => "!self",
            < 80 => "!activator",
            < 86 => $"nobody{random.Next(3)}",
            < 93 => $"e{random.Next(1, 7)}*",
            < 96 => "zz*",
            _ => "",
        };

        return new EntityConnection(
            Pick(random, Outputs(classes[sender])),
            target,
            Pick(random, Inputs(classes[receiver])),
            random.Next(5) == 0 ? new string('x', random.Next(1, 41)) : "",
            random.Next(4) == 0 ? random.Next(1, 50) / 4f : 0f,
            random.Next(5) == 0 ? random.Next(1, 4) : EntityConnection.Infinite);
    }

    private static string Pick(Random random, IReadOnlyList<string> names) =>
        names.Count == 0 || random.Next(10) == 0 ? "Mystery" : names[random.Next(names.Count)];

    private static IReadOnlyList<string> Outputs(string className) =>
        LogicFixture.Catalog.TryGetSchema(className, out EntitySchema? schema) ? schema.Outputs : [];

    private static IReadOnlyList<string> Inputs(string className) =>
        LogicFixture.Catalog.TryGetSchema(className, out EntitySchema? schema) ? schema.Inputs : [];
}
