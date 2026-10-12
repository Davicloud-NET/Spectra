using System;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// The built-in classes a script knows a node by, and which of them extends
/// which. A script's <c>IsA</c> goes by this: a part is also a BasePart, and
/// every node is an Instance.
/// </summary>
public static class NodeClass
{
    /// <summary>The class every node is of.</summary>
    public const string Instance = "Instance";

    /// <summary>What a part and a mesh both are.</summary>
    public const string BasePart = "BasePart";

    /// <summary>What every kind of light is.</summary>
    public const string Light = "Light";

    /// <summary>
    /// The built-in class of a live node. An entity counts as what carries
    /// it, so a door is a Part. Render thread only.
    /// </summary>
    // The classifier's order with the entity left out: a brush, a light, a
    // mesh, a script. A node of a baked level is still what the bake took
    // from it.
    public static NodeClassKind KindOf(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Brush is not null || node.BakedKind is SceneNodeKind.BrushWorld or SceneNodeKind.BrushSubtractive)
            return NodeClassKind.Part;
        if (node.Light is { } light)
            return OfLight(light.Kind);
        if (node.MeshRenderer is not null || node.BakedKind == SceneNodeKind.Mesh)
            return NodeClassKind.MeshPart;
        if (node.Script is { } script)
            return script.IsModule ? NodeClassKind.ModuleScript : NodeClassKind.Script;

        // An entity whose class was never set is no entity to a script.
        return node.Entity is { ClassName.Length: > 0 } ? NodeClassKind.Instance : NodeClassKind.Folder;
    }

    /// <summary>The class of a light of that kind.</summary>
    public static NodeClassKind OfLight(LightKind kind) => kind switch
    {
        LightKind.Directional => NodeClassKind.DirectionalLight,
        LightKind.Spot => NodeClassKind.SpotLight,
        LightKind.Rect => NodeClassKind.SurfaceLight,
        LightKind.Disc => NodeClassKind.DiscLight,
        _ => NodeClassKind.PointLight,
    };

    /// <summary>What a script calls a built-in class.</summary>
    // Roblox has a word for three of the lights. The other two are named the same way.
    public static string NameOf(NodeClassKind kind) => kind switch
    {
        NodeClassKind.Folder => NodeClassName.Folder,
        NodeClassKind.Part => NodeClassName.Part,
        NodeClassKind.MeshPart => NodeClassName.MeshPart,
        NodeClassKind.PointLight => "PointLight",
        NodeClassKind.SpotLight => "SpotLight",
        NodeClassKind.DirectionalLight => "DirectionalLight",
        NodeClassKind.SurfaceLight => "SurfaceLight",
        NodeClassKind.DiscLight => "DiscLight",
        NodeClassKind.Script => NodeClassName.Script,
        NodeClassKind.ModuleScript => NodeClassName.ModuleScript,
        _ => Instance,
    };

    /// <summary>
    /// The class a built-in class extends. Null for Instance, and for a name
    /// that is no built-in class, as an entity class is none.
    /// </summary>
    public static string? BaseOf(ReadOnlySpan<char> className) => className switch
    {
        NodeClassName.Part or NodeClassName.MeshPart => BasePart,
        "PointLight" or "SpotLight" or "DirectionalLight" or "SurfaceLight" or "DiscLight" => Light,
        BasePart or Light or NodeClassName.Folder or NodeClassName.Script or NodeClassName.ModuleScript => Instance,
        _ => null,
    };

    /// <summary>
    /// Whether the node is of that class or of one that extends it. An
    /// entity is of its own class too. Render thread only.
    /// </summary>
    public static bool IsA(SceneNode node, ReadOnlySpan<char> className)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (className.SequenceEqual(Instance))
            return true;
        if (node.Entity is { ClassName.Length: > 0 } entity && className.SequenceEqual(entity.ClassName))
            return true;

        for (string? at = NameOf(KindOf(node)); at is not null; at = BaseOf(at))
        {
            if (className.SequenceEqual(at))
                return true;
        }

        return false;
    }
}
