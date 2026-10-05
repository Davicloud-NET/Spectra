using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace SpectraEngine.Core.Entities;

// ent_list and ent_show. Both read the running entities while a level plays
// and the placed ones while it does not, and change nothing.
internal static class EntityListing
{
    public const int MaxListed = 200;
    public const int MaxShown = 5;

    public static void List(in ConArgs args)
    {
        if (args.Count > 1)
        {
            args.Out.Error(
                "ent_list: give one name, class or prefix ending in *. Put a name with spaces in quotes.");
            return;
        }

        if (!TryFindLevel(in args, "ent_list", out Scene.Scene? scene, out EntityWorld? world))
            return;

        string? pattern = args.Count == 1 ? args[0].ToString() : null;
        var matches = new List<ListedEntity>();
        int total = Collect(scene, world, pattern, matches);

        args.Out.Print(ListHeader(matches.Count, total, pattern, world, args.IsPlaying));

        int shown = Math.Min(matches.Count, MaxListed);
        for (int i = 0; i < shown; i++)
            args.Out.Print(ListRow(matches[i], scene.EntitySchemas));

        if (matches.Count > shown)
        {
            args.Out.Print(string.Create(
                CultureInfo.InvariantCulture,
                $"ent_list: {matches.Count - shown} more not shown. Give a pattern to narrow the list."));
        }
    }

    public static void Show(in ConArgs args)
    {
        if (args.Count != 1 || args[0].IsEmpty)
        {
            args.Out.Error(
                "ent_show: give one name, class or prefix ending in *. Put a name with spaces in quotes. " +
                "ent_list shows them.");
            return;
        }

        if (!TryFindLevel(in args, "ent_show", out Scene.Scene? scene, out EntityWorld? world))
            return;

        string pattern = args[0].ToString();
        var matches = new List<ListedEntity>();
        Collect(scene, world, pattern, matches);

        if (matches.Count == 0)
        {
            string what = TargetNamePattern.IsPrefix(pattern)
                ? $"no name or class matches '{pattern}'"
                : $"nothing has the name or the class '{pattern}'";
            args.Out.Error($"ent_show: {what}. Names are case sensitive. ent_list shows them.");
            return;
        }

        var description = new EntityDescription(args.Out, scene, world);
        int shown = Math.Min(matches.Count, MaxShown);
        for (int i = 0; i < shown; i++)
            description.Write(matches[i]);

        if (matches.Count > shown)
        {
            string left = EntityConsoleText.Count(
                matches.Count - shown, "more entity matches", "more entities match");
            args.Out.Print(
                $"ent_show: {left} {EntityConsoleText.Name(pattern)}. Narrow the pattern to see them.");
        }

        if (world is null && args.IsPlaying)
            args.Out.Warn($"ent_show: {EntityConsoleText.LevelReplaced}");
    }

    // The running world when there is one, and the scene either way.
    private static bool TryFindLevel(
        in ConArgs args,
        string command,
        [NotNullWhen(true)] out Scene.Scene? scene,
        out EntityWorld? world)
    {
        world = args.Entities is { IsActive: true } running ? running : null;
        scene = world?.Scene ?? args.Scene;
        if (scene is not null)
            return true;

        args.Out.Error($"{command}: no level is open.");
        return false;
    }

    // Returns how many entities there are, matching or not.
    private static int Collect(
        Scene.Scene scene,
        EntityWorld? world,
        string? pattern,
        List<ListedEntity> matches)
    {
        int total = 0;

        if (world is not null)
        {
            foreach (Entity entity in world.Entities)
            {
                total++;
                if (Passes(pattern, entity.TargetName, entity.ClassName))
                    matches.Add(new ListedEntity(entity.TargetName, entity.Data, entity.Node.Id, entity));
            }

            return total;
        }

        foreach (SceneNode node in scene.Root.Traverse())
        {
            if (node.Entity is not { } data)
                continue;

            total++;
            if (Passes(pattern, node.Name, data.ClassName))
                matches.Add(new ListedEntity(node.Name, data, node.Id, null));
        }

        return total;
    }

    private static bool Passes(string? pattern, string name, string className) =>
        pattern is null || EntityConsoleText.MatchesNameOrClass(pattern, name, className);

    private static string ListHeader(int matched, int total, string? pattern, EntityWorld? world, bool isPlaying)
    {
        CultureInfo invariant = CultureInfo.InvariantCulture;
        string state = world is null ? "placed" : "running";
        var header = new StringBuilder("ent_list: ");

        if (pattern is null)
            header.Append(invariant, $"{total} {state}");
        else
            header.Append(invariant, $"{matched} of {total} {state} match {EntityConsoleText.Name(pattern)}");

        if (world is null)
        {
            header.Append(", not running");
            if (isPlaying)
                header.Append(": ").Append(EntityConsoleText.LevelReplaced);
        }
        else if (pattern is null)
        {
            string waiting = EntityConsoleText.Count(world.PendingEventCount, "event", "events");
            header.Append(invariant, $", tick {world.TickNumber} ({world.Time:0.##} s), {waiting} waiting");
        }

        return header.ToString();
    }

    private static string ListRow(in ListedEntity entity, EntitySchemaCatalog? schemas)
    {
        string name = EntityConsoleText.Name(entity.Name);
        if (entity.ClassName.Length == 0)
            return $"{name}  (no class)";

        return entity.IsUnknownClass(schemas)
            ? $"{name}  {entity.ClassName}  (class not in this build)"
            : $"{name}  {entity.ClassName}";
    }
}
