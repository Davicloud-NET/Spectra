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

        return Of(
            SceneNodeClassifier.Classify(node),
            node.Entity?.ClassName,
            node.Light?.Kind,
            node.Script is { IsModule: true });
    }

    /// <summary>The same from what a UI holds of a node.</summary>
    /// <param name="kind">What the node is.</param>
    /// <param name="entityClass">Its entity class, or null when it carries no entity.</param>
    /// <param name="light">Its kind of light, or null when it carries none.</param>
    /// <param name="isModule">Whether its script is a module.</param>
    public static string Of(SceneNodeKind kind, string? entityClass, LightKind? light, bool isModule = false)
    {
        // First, so a door is a func_door and not the part it is made of.
        if (!string.IsNullOrEmpty(entityClass))
            return entityClass;

        return kind switch
        {
            SceneNodeKind.BrushWorld or SceneNodeKind.BrushPart or SceneNodeKind.BrushSubtractive => Part,
            SceneNodeKind.Mesh => MeshPart,
            SceneNodeKind.Light => OfLight(light ?? LightKind.Point),
            SceneNodeKind.Script => isModule ? ModuleScript : Script,
            _ => Folder,
        };
    }

    // Roblox has a word for three of them. The other two are named the same way.
    private static string OfLight(LightKind kind) => kind switch
    {
        LightKind.Directional => "DirectionalLight",
        LightKind.Spot => "SpotLight",
        LightKind.Rect => "SurfaceLight",
        LightKind.Disc => "DiscLight",
        _ => "PointLight",
    };
}
