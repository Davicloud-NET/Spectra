namespace SpectraEngine.Core.Scene;

/// <summary>
/// The built-in class a node counts as to a script, by what it carries.
/// <see cref="NodeClass.KindOf"/> answers it for a node.
/// </summary>
public enum NodeClassKind : byte
{
    /// <summary>An entity that carries nothing else. It is of its entity class and of no built-in one.</summary>
    Instance,

    /// <summary>A node with no payload, with children or without.</summary>
    Folder,

    /// <summary>A brush of any kind.</summary>
    Part,

    /// <summary>A node that draws a mesh.</summary>
    MeshPart,

    /// <summary>A light that shines from a point.</summary>
    PointLight,

    /// <summary>A light that shines in a cone.</summary>
    SpotLight,

    /// <summary>A light that shines one way everywhere, like the sun.</summary>
    DirectionalLight,

    /// <summary>A light that shines from a rectangle.</summary>
    SurfaceLight,

    /// <summary>A light that shines from a disc.</summary>
    DiscLight,

    /// <summary>A script that runs when the level starts.</summary>
    Script,

    /// <summary>A script that runs when another script asks for it.</summary>
    ModuleScript,
}
