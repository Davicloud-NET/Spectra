using System;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// The class name a node shows to a script: its entity class when it has
/// one, otherwise its kind in the words Roblox uses.
/// </summary>
public static class NodeClassName
{
    /// <summary>A brush of any kind.</summary>
    public const string Part = "Part";

    /// <summary>A node that draws a mesh.</summary>
    public const string MeshPart = "MeshPart";

    /// <summary>A node with no payload, with children or without.</summary>
    public const string Folder = "Folder";

    /// <summary>A script that runs when the level starts.</summary>
    public const string Script = "Script";

    /// <summary>A script that runs when another script asks for it.</summary>
    public const string ModuleScript = "ModuleScript";

    /// <summary>The class name of a live node. Render thread only.</summary>
    public static string Of(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // First, so a door is a func_door and not the part it is made of.
        return node.Entity is { ClassName.Length: > 0 } entity
            ? entity.ClassName
            : NodeClass.NameOf(NodeClass.KindOf(node));
    }
}
