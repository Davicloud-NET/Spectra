using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>A node of the level's tree: a part, a light, a folder, a script.</summary>
// A property with Sets has no body here. The generator writes it, and it
// reads the member of ScriptWrites that a script's write goes through.
[ScriptClass("Instance")]
internal static partial class NodeApi
{
    /// <summary>The node's name. A wire finds its entity by the same name.</summary>
    [ScriptProperty(Sets = SettableMemberId.Name)]
    public static partial string Name(SceneNode node);

    /// <summary>The node's class: its entity class when it has one, otherwise its kind, such as Part or PointLight.</summary>
    [ScriptProperty]
    public static string ClassName(SceneNode node) => NodeClassName.Of(node);

    /// <summary>The node above this one in the tree. Nil for the root.</summary>
    [ScriptProperty(CannotSet = "Parent cannot be set from a script yet")]
    public static SceneNode? Parent(SceneNode node) => node.Parent;

    /// <summary>Where the node is in the world.</summary>
    [ScriptProperty(Sets = SettableMemberId.Position)]
    public static partial Vector3 Position(SceneNode node);

    /// <summary>Where the node is in the world and which way it is turned.</summary>
    [ScriptProperty(Sets = SettableMemberId.CFrame)]
    public static partial CFrame CFrame(SceneNode node);

    /// <summary>Where the node is under its parent.</summary>
    [ScriptProperty(Sets = SettableMemberId.LocalPosition)]
    public static partial Vector3 LocalPosition(SceneNode node);

    /// <summary>Where the node is under its parent and which way it is turned there.</summary>
    [ScriptProperty(Sets = SettableMemberId.LocalCFrame)]
    public static partial CFrame LocalCFrame(SceneNode node);
}
