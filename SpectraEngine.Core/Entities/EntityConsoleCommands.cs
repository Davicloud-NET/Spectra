using SpectraEngine.Core.ConsoleSystem;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The console commands for entity wiring: <c>ent_fire</c>, <c>ent_list</c>,
/// <c>ent_show</c> and <c>ent_watch</c>.
/// </summary>
public static class EntityConsoleCommands
{
    /// <summary>Adds the four commands to <paramref name="table"/>.</summary>
    /// <param name="table">The console's commands.</param>
    /// <param name="watch">
    /// What <c>ent_watch</c> sets and <c>ent_fire</c> reports through. It must
    /// write to the same console.
    /// </param>
    public static void Register(ConCommandTable table, EntityWatch watch)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(watch);

        table.Add(new ConCommand(
            "ent_fire",
            "ent_fire <target> <input> [parameter] [delay]",
            "Sends an input to every entity a name matches. The delay is in seconds. " +
            "For a delay with no parameter, pass \"\".",
            (in ConArgs args) => Fire(watch, in args)));
        table.Add(new ConCommand(
            "ent_list",
            "ent_list [pattern]",
            "Lists entities by name and class.",
            EntityListing.List));
        table.Add(new ConCommand(
            "ent_show",
            "ent_show <pattern>",
            "Prints an entity's keyvalues, state, wires and inputs.",
            EntityListing.Show));
        table.Add(new ConCommand(
            "ent_watch",
            "ent_watch [on | off | <pattern> ...]",
            "Prints what entities fire and receive as it happens, for every entity or the ones named.",
            (in ConArgs args) => Watch(watch, in args)));
    }

    private static void Fire(EntityWatch watch, in ConArgs args)
    {
        if (RefuseSyntax(in args, out float delay) is { } misuse)
        {
            args.Out.Error(misuse);
            return;
        }

        if (args.Entities is not { Index: { } index } world)
        {
            args.Out.Error(args.IsPlaying
                ? $"ent_fire: {EntityConsoleText.LevelReplaced}"
                : "ent_fire: the level is not running. Press F8, or type play on in the editor, then fire again.");
            return;
        }

        // Counted here, in a list of our own and outside any tick. The world
        // resolves the name again when the input comes due.
        string target = args[0].ToString();
        var matched = new List<Entity>();
        index.Resolve(target, null, null, null, matched);
        if (matched.Count == 0)
        {
            args.Out.Error(NothingToFireAt(world, target));
            return;
        }

        string input = args[1].ToString();
        if (RefuseInput(world.Scene.EntitySchemas, matched, target, input) is { } refusal)
        {
            args.Out.Error(refusal);
            return;
        }

        string parameter = args[2].ToString();
        watch.ExpectHandFired(world, delay);
        world.QueueInput(target, input, parameter, delay);

        var reply = new StringBuilder("ent_fire: ");
        EntityConsoleText.AppendCall(reply, input, parameter);
        reply.Append(" queued for ").Append(EntityConsoleText.Reach(matched.Count, target));
        if (delay > 0f)
            reply.Append(", in ").Append(EntityConsoleText.Seconds(delay)).Append('s');
        args.Out.Print(reply.ToString());
    }

    private static string NothingToFireAt(EntityWorld world, string target)
    {
        if (TargetNamePattern.IsPrefix(target))
            return $"ent_fire: nothing matches '{target}'. Names are case sensitive. ent_list shows them.";

        // A wire does not resolve a class, so neither does this.
        foreach (Entity entity in world.Entities)
        {
            if (string.Equals(entity.ClassName, target, StringComparison.Ordinal))
            {
                return $"ent_fire: nothing is named '{target}'. It is a class, and a target is a name. " +
                    $"ent_list {EntityConsoleText.Name(target)} shows the names to fire at.";
            }
        }

        return $"ent_fire: nothing is named '{target}'. Names are case sensitive. ent_list shows them.";
    }

    // What is wrong with the line whatever the level holds, or null.
    private static string? RefuseSyntax(in ConArgs args, out float delay)
    {
        delay = 0f;

        if (args.Count < 2 || args[0].IsEmpty || args[1].IsEmpty)
        {
            return "ent_fire: give a target and an input, like ent_fire door1 Open. " +
                "A parameter and a delay in seconds may follow.";
        }

        if (args.Count > 4)
            return "ent_fire: too many arguments. Put a name or a parameter with spaces in quotes.";

        if (args[0][0] == '!')
            return RefuseToken(args[0].ToString());

        if (args.Count > 3 && (!args.TryGetFloat(3, out delay) || delay < 0f))
        {
            return $"ent_fire: '{args[3]}' is not a delay. Give seconds, zero or more, " +
                "with a dot for the decimal point, like 0.5.";
        }

        return null;
    }

    private static string RefuseToken(string token) => token switch
    {
        TargetNameIndex.SelfToken =>
            "ent_fire: !self is the entity whose output is firing. There is none on the console. Name an entity.",
        TargetNameIndex.ActivatorToken =>
            "ent_fire: !activator is whoever started a chain of outputs. There is none on the console. " +
            "Name an entity.",
        TargetNameIndex.CallerToken =>
            "ent_fire: !caller is the entity whose output sent an input. There is none on the console. " +
            "Name an entity.",
        _ => $"ent_fire: '{token}' is not a name. A target that starts with ! is !self, !activator or " +
            "!caller, and none of them means anything on the console. Name an entity.",
    };

    // Refuses only when every class the target reaches lists its inputs and
    // none lists this one. A schema can be incomplete, so anything less is
    // queued and the delivery says what happened.
    private static string? RefuseInput(
        EntitySchemaCatalog? schemas,
        List<Entity> matched,
        string target,
        string input)
    {
        if (schemas is null)
            return null;

        var classes = new List<EntitySchema>();
        foreach (Entity entity in matched)
        {
            if (!schemas.TryGetSchema(entity.ClassName, out EntitySchema? schema)
                || schema.Inputs.Count == 0
                || Find(schema.Inputs, input, StringComparison.Ordinal) is not null)
            {
                return null;
            }

            if (!classes.Contains(schema))
                classes.Add(schema);
        }

        string? meant = null;
        for (int i = 0; i < classes.Count && meant is null; i++)
            meant = Find(classes[i].Inputs, input, StringComparison.OrdinalIgnoreCase);
        string hint = meant is null ? "" : $" Did you mean {meant}?";

        if (classes.Count == 1)
        {
            return $"ent_fire: {classes[0].ClassName} has no input '{input}'.{hint} " +
                $"Inputs: {string.Join(' ', classes[0].Inputs)}";
        }

        string shown = EntityConsoleText.Name(target);
        return $"ent_fire: {shown} reaches {EntityConsoleText.Count(classes.Count, "class", "classes")} " +
            $"and none has an input '{input}'.{hint} ent_show {shown} lists their inputs.";
    }

    private static string? Find(IReadOnlyList<string> names, string name, StringComparison comparison)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, comparison))
                return names[i];
        }

        return null;
    }

    // Every form sets the whole state. There is no toggle.
    private static void Watch(EntityWatch watch, in ConArgs args)
    {
        if (args.Count == 1 && args[0].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            watch.TurnOff();
            args.Out.Print("ent_watch: off");
            return;
        }

        if (args.Count == 0 || (args.Count == 1 && args[0].Equals("on", StringComparison.OrdinalIgnoreCase)))
        {
            watch.TurnOn();
        }
        else
        {
            var patterns = new string[args.Count];
            for (int i = 0; i < patterns.Length; i++)
            {
                patterns[i] = args[i].ToString();
                if (patterns[i].Length == 0 || patterns[i][0] == '!')
                {
                    args.Out.Error(
                        $"ent_watch: '{patterns[i]}' never matches an entity. " +
                        "Give a name, a class or a prefix ending in *.");
                    return;
                }
            }

            watch.TurnOn(patterns);
        }

        args.Out.Print(DescribeWatch(watch, in args));
    }

    private static string DescribeWatch(EntityWatch watch, in ConArgs args)
    {
        var reply = new StringBuilder("ent_watch: on, ");
        IReadOnlyList<string> patterns = watch.Patterns;

        if (patterns.Count == 0)
            reply.Append("every entity");
        else
            reply.Append("names matching ");

        for (int i = 0; i < patterns.Count; i++)
        {
            if (i > 0)
                reply.Append(i == patterns.Count - 1 ? " or " : ", ");
            EntityConsoleText.AppendName(reply, patterns[i]);
        }

        if (args.Entities is null)
        {
            reply.Append(". Nothing prints until the level runs");
            reply.Append(args.IsPlaying ? $": {EntityConsoleText.LevelReplaced}" : ".");
        }

        return reply.ToString();
    }
}
