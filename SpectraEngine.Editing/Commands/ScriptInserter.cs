using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Adds a script node to the level: one that names a file that is there, or
/// one whose file is made first. Render thread only.
/// </summary>
public static class ScriptInserter
{
    /// <summary>What a new script node is called.</summary>
    public const string NodeName = "Script";

    /// <summary>The history entry's name.</summary>
    public const string CommandName = "Insert Script";

    /// <summary>
    /// Where a script goes when nobody pointed at a node: under the selected
    /// node, or under the root with nothing selected. False with several selected.
    /// </summary>
    /// <param name="refusal">Why there is no parent, as the end of a sentence.</param>
    // A script acts on its parent, and the order nodes were selected in shows
    // nowhere. Taking the first would be a guess that leaves a file behind.
    public static bool TryFindParent(Scene scene, [NotNullWhen(true)] out SceneNode? parent, out string refusal)
    {
        ArgumentNullException.ThrowIfNull(scene);

        IReadOnlyList<SceneNode> selected = scene.Selection.Items;
        refusal = string.Empty;
        parent = selected.Count switch
        {
            0 => scene.Root,
            1 => ParentFor(scene, selected[0]),
            _ => null,
        };

        if (parent is null)
            refusal = $"{selected.Count} nodes are selected. Select the one the script should go under.";

        return parent is not null;
    }

    // Insert leaves the new script selected. Pressed again, it would nest the
    // next one under a script, which nobody asks for by pressing twice.
    private static SceneNode ParentFor(Scene scene, SceneNode selected) =>
        SceneNodeClassifier.HasPlace(selected) ? selected : selected.Parent ?? scene.Root;

    /// <summary>
    /// Makes a script file, adds a script that names it as one history entry
    /// and selects it. The file stays when the entry is undone.
    /// </summary>
    /// <param name="contentRoot">The folder the level's content paths are relative to.</param>
    /// <param name="text">What the new file holds.</param>
    public static ScriptInsertReport InsertNew(Scene scene, UndoStack undo, string contentRoot, string text)
    {
        ArgumentNullException.ThrowIfNull(undo);

        if (!TryFindParent(scene, out SceneNode? parent, out string refusal))
            return ScriptInsertReport.RefusedBecause(string.Empty, refusal);

        // Every root is called Root. A level's script is named after the level.
        string named = ReferenceEquals(parent, scene.Root) ? scene.Name : parent.Name;

        if (!ScriptFileMaker.TryMake(contentRoot, named, text, out string contentPath, out string failure))
            return ScriptInsertReport.RefusedBecause(string.Empty, failure);

        return Add(scene, undo, parent, new Script(contentPath)) with { MadeFile = true };
    }

    /// <summary>
    /// Adds a script that names a file under <paramref name="parent"/>, as one
    /// history entry, and selects it.
    /// </summary>
    /// <param name="contentPath">The file, relative to the content root.</param>
    public static ScriptInsertReport Attach(Scene scene, UndoStack undo, SceneNode parent, string contentPath)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(contentPath);

        Script script;
        try
        {
            script = new Script(contentPath);
        }
        catch (ArgumentException)
        {
            return ScriptInsertReport.RefusedBecause(contentPath, "that is not a path inside the Assets folder.");
        }

        return Add(scene, undo, parent, script);
    }

    private static ScriptInsertReport Add(Scene scene, UndoStack undo, SceneNode parent, Script script)
    {
        var node = new SceneNode(NodeName) { Script = script };

        undo.Execute(new AddNodesCommand([new NodePlacement(node, parent.Id, parent.Children.Count)])
        {
            Name = CommandName,
        });

        scene.Selection.Select(node);

        return new ScriptInsertReport(script.Path, node.Id, parent.Name, null)
        {
            IsLevelScript = ReferenceEquals(parent, scene.Root),
        };
    }
}
