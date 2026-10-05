using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Hosting;

/// <summary>
/// Finds the entities a mapper has to fix before the level plays right.
/// Render thread only: it reads live nodes.
/// </summary>
public static class EntityAudit
{
    /// <summary>
    /// Every entity of a brush or volume class with a world brush at or below
    /// its node, in scene order. A class the scene's catalogue does not know
    /// is skipped.
    /// </summary>
    // The same test the runtime's move makes, so the list is what would be
    // refused at play time.
    public static List<EntityProblem> FindWorldBrushOwners(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var problems = new List<EntityProblem>();
        if (scene.EntitySchemas is not { } schemas)
            return problems;

        foreach (SceneNode node in scene.Root.Traverse())
        {
            if (node.Entity is not { } entity || node.SubtreeStaticWorldBrushCount == 0)
                continue;

            if (!schemas.TryGetSchema(entity.ClassName, out EntitySchema? schema)
                || !EntityEditor.UsesGeometry(schema.Placement))
            {
                continue;
            }

            foreach (SceneNode below in node.Traverse())
            {
                if (!below.IsStaticWorldBrush || below.Brush is not { } brush)
                    continue;

                problems.Add(new EntityProblem(
                    node.Id, node.Name, entity.ClassName,
                    below.Id, below.Name, brush.Operation == BrushOperation.Subtractive));
                break;
            }
        }

        return problems;
    }
}
