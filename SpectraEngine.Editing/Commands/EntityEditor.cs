using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using NodeFlags = SpectraEngine.Editing.Commands.SetNodeFlagsCommand.NodeFlags;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Turns geometry into an entity and back. Each verb is one undo entry.
/// </summary>
// An entity's brushes are parts at or below its node. A world brush anywhere
// below stops the entity moving when the level plays, so Make converts all of
// them, and stamps flags only on the brushes the entity owns.
public static class EntityEditor
{
    /// <summary>The undo entry name for making an entity.</summary>
    public const string MakeName = "Make Entity";

    /// <summary>The undo entry name for removing one.</summary>
    public const string RemoveName = "Remove Entity";

    /// <summary>Whether a class placed this way is made from brush geometry.</summary>
    public static bool UsesGeometry(EntityPlacement placement) =>
        placement is EntityPlacement.Brush or EntityPlacement.Volume;

    /// <summary>
    /// <paramref name="current"/> with the collide, query, touch and drawn
    /// flags a brush of this placement carries. A volume senses and is neither
    /// drawn nor solid. Everything else gets the defaults.
    /// </summary>
    public static NodeFlags FlagsFor(EntityPlacement placement, NodeFlags current)
    {
        bool solid = placement != EntityPlacement.Volume;

        return current
            .With(PhysicsFlags.CanCollide, solid)
            .With(PhysicsFlags.CanQuery, solid)
            .With(PhysicsFlags.CanTouch, true)
            with { IsRendered = solid };
    }

    /// <summary>
    /// A detached part box carrying an entity of the schema's class, flagged
    /// for its placement and named after the class with the first free number.
    /// </summary>
    /// <param name="halfExtent">Half the box's edge length.</param>
    public static SceneNode BuildGeometryNode(Scene scene, EntitySchema schema, float halfExtent)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(schema);

        // Kind before brush, so the brush never counts as world geometry.
        var node = new SceneNode(NextName(scene, schema.ClassName)) { BrushKind = BrushKind.Part };
        node.Brush = Brush.CreateBox(new Vector3(-halfExtent), new Vector3(halfExtent));
        FlagsFor(schema.Placement, NodeFlags.From(node)).ApplyTo(node);
        node.Entity = new EntityData(schema.ClassName);
        return node;
    }

    /// <summary>
    /// Makes the one selected block, part or group an entity of
    /// <paramref name="className"/>: its blocks become parts with their
    /// textures kept in place, the brushes it owns take the placement's flags,
    /// and the node takes the entity. The node keeps its name.
    /// </summary>
    public static EntityEditReport Make(
        Scene scene, UndoStack undo, IReadOnlyList<SceneNode> selection, string className)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(selection);

        if (!CanMake(scene, selection, className, out EntitySchema? schema, out string refusal))
            return EntityEditReport.RefusedBecause(refusal);

        SceneNode node = selection[0];
        var commands = new List<IEditorCommand>();
        int converted = 0;

        foreach (SceneNode below in node.Traverse())
        {
            if (BrushKindConversion.AppendToPart(below, commands))
                converted++;
        }

        foreach (SceneNode owned in OwnedBrushes(node))
            AppendFlags(owned, FlagsFor(schema.Placement, NodeFlags.From(owned)), commands);

        commands.Add(new SetEntityCommand(node.Id, null, new EntityData(schema.ClassName)));
        undo.Execute(new CompositeCommand(MakeName, commands));

        string message = $"'{node.Name}' is now a {schema.ClassName}.";
        if (converted > 0)
            message += converted == 1 ? " 1 block became a part." : $" {converted} blocks became parts.";
        if (schema.Placement == EntityPlacement.Volume)
            message += " It is no longer drawn or solid. Its outline shows where it is.";

        return EntityEditReport.Done(message);
    }

    /// <summary>
    /// Takes the entity off every selected node that carries one and puts the
    /// flags of the brushes it owned back to the defaults. Parts stay parts.
    /// </summary>
    public static EntityEditReport Remove(Scene scene, UndoStack undo, IReadOnlyList<SceneNode> selection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(selection);

        var commands = new List<IEditorCommand>();
        SceneNode? last = null;
        string className = string.Empty;
        int removed = 0;

        foreach (SceneNode node in selection)
        {
            if (node.Entity is not { } entity)
                continue;

            foreach (SceneNode owned in OwnedBrushes(node))
                AppendFlags(owned, FlagsFor(EntityPlacement.Brush, NodeFlags.From(owned)), commands);

            commands.Add(SetEntityCommand.Capture(node, null));
            last = node;
            className = entity.ClassName;
            removed++;
        }

        if (last is null)
        {
            return EntityEditReport.RefusedBecause(selection.Count == 0
                ? "Remove entity needs an entity. Select one first."
                : "Nothing selected is an entity, so there is nothing to remove.");
        }

        undo.Execute(new CompositeCommand(RemoveName, commands));

        if (removed > 1)
            return EntityEditReport.Done($"{removed} nodes are no longer entities. Their parts stay parts.");

        return EntityEditReport.Done(last.SubtreeBrushCount > 0
            ? $"'{last.Name}' is no longer a {className}. It stays a part."
            : $"'{last.Name}' is no longer a {className}.");
    }

    // Each refusal names the way out.
    private static bool CanMake(
        Scene scene,
        IReadOnlyList<SceneNode> selection,
        string className,
        [NotNullWhen(true)] out EntitySchema? schema,
        out string refusal)
    {
        schema = null;
        refusal = WhyNotSelection(selection) ?? string.Empty;
        if (refusal.Length > 0)
            return false;

        if (scene.EntitySchemas is not { } schemas || !schemas.TryGetSchema(className, out schema))
        {
            refusal = $"This project has no entity class named '{className}'.";
            return false;
        }

        if (!UsesGeometry(schema.Placement))
        {
            refusal = $"A {schema.ClassName} is not made from geometry. Use Insert, Entity for it.";
            return false;
        }

        refusal = WhyNotNode(selection[0]) ?? string.Empty;
        return refusal.Length == 0;
    }

    private static string? WhyNotSelection(IReadOnlyList<SceneNode> selection) => selection.Count switch
    {
        0 => "Make entity needs a block, a part or a group of them. Select one first.",
        1 => null,
        _ => $"Make entity works on one thing at a time. Group the {selection.Count} selected " +
            "nodes first (Ctrl+G), then make the group an entity.",
    };

    private static string? WhyNotNode(SceneNode node)
    {
        if (node.Entity is { } existing)
        {
            return $"'{node.Name}' is already a {existing.ClassName}. Use Remove entity first, " +
                "then make it something else.";
        }

        if (node.SubtreeBrushCount == 0)
            return $"'{node.Name}' has no block or part in it. Select a block, a part or a group of them.";

        // A cut made a part stops cutting, and the level changes shape.
        foreach (SceneNode below in node.Traverse())
        {
            if (below.Brush is not { Operation: BrushOperation.Subtractive })
                continue;

            return ReferenceEquals(below, node)
                ? $"'{node.Name}' cuts solid, and a cut only works as world geometry. " +
                  "Set its Operation to Adds solid first."
                : $"'{below.Name}' cuts solid, and a cut only works as world geometry. " +
                  $"Move it out of '{node.Name}' or set its Operation to Adds solid, then try again.";
        }

        return null;
    }

    // The same walk the runtime does at spawn: the node's own brush and those
    // below it, stopping at a node that carries an entity of its own.
    private static List<SceneNode> OwnedBrushes(SceneNode node)
    {
        var owned = new List<SceneNode>();
        if (node.Brush is not null)
            owned.Add(node);

        CollectBelow(node, owned);
        return owned;

        static void CollectBelow(SceneNode parent, List<SceneNode> into)
        {
            IReadOnlyList<SceneNode> children = parent.Children;
            for (int i = 0; i < children.Count; i++)
            {
                SceneNode child = children[i];
                if (child.Entity is not null || child.SubtreeBrushCount == 0)
                    continue;

                if (child.Brush is not null)
                    into.Add(child);

                CollectBelow(child, into);
            }
        }
    }

    private static void AppendFlags(SceneNode node, NodeFlags flags, List<IEditorCommand> into)
    {
        if (NodeFlags.From(node) != flags)
            into.Add(SetNodeFlagsCommand.Capture(node, flags));
    }

    // The name is what a wire targets, so a fresh door must not share one.
    private static string NextName(Scene scene, string className)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (SceneNode node in scene.Root.Traverse())
        {
            if (node.Entity is not null)
                taken.Add(node.Name);
        }

        for (int number = 1; ; number++)
        {
            string name = className + number.ToString(CultureInfo.InvariantCulture);
            if (!taken.Contains(name))
                return name;
        }
    }
}
