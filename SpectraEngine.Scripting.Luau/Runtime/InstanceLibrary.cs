using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>Makes a new node while the level plays. Stop takes away every node a script made.</summary>
[ScriptLibrary("Instance")]
internal sealed partial class InstanceLibrary(NodeLifetime lifetime)
{
    /// <summary>Makes a node of a class, named after its class. It is in no level until it gets a Parent, and with a second argument it gets that one at once. A Part is a solid cube one unit to a side. A light is white, with a Brightness of 1 and a Range of 8, or 16 for a SpotLight, a SurfaceLight and a DiscLight. Any other node is cloned from one of the level. Scripts hold at most 16384 nodes they made at a time, and Destroy frees one.</summary>
    // Written by hand: what comes back is of the class the name says.
    [ScriptFunction(Name = "new")]
    [ScriptOverload("className: \"Part\", parent: Instance?", "Part")]
    [ScriptOverload("className: \"Folder\", parent: Instance?", "Folder")]
    [ScriptOverload("className: \"PointLight\", parent: Instance?", "PointLight")]
    [ScriptOverload("className: \"SpotLight\", parent: Instance?", "SpotLight")]
    [ScriptOverload("className: \"SurfaceLight\", parent: Instance?", "SurfaceLight")]
    [ScriptOverload("className: \"DirectionalLight\", parent: Instance?", "DirectionalLight")]
    [ScriptOverload("className: \"DiscLight\", parent: Instance?", "DiscLight")]
    private int New(ScriptContext context, LuauStack stack)
    {
        if (!stack.TryGetUtf8(1, out ReadOnlySpan<byte> utf8))
            return ScriptArguments.Wrong(stack, 1, "new", "string");

        SceneNode? parent = null;
        if (!stack.IsNil(2) && !context.TryGetObject(stack, 2, out parent))
            return ScriptArguments.Wrong(stack, 2, "new", "Instance");

        Span<char> room = stackalloc char[NameChars.Room];
        if (!NameChars.TryRead(utf8, room, out ReadOnlySpan<char> className))
            return stack.Fail("Instance.new was handed a class name that is not valid UTF-8");
        if (lifetime.Make(className, out string? refusal) is not { } made)
            return stack.Fail(refusal ?? "Instance.new made nothing");
        if (parent is null)
            return ScriptResult.Of(context.PushObject(stack, made));

        // A node that cannot have the parent it was made for is not kept.
        if (!context.Tree.TrySetParent(made, parent, out string? why))
        {
            lifetime.Destroy(made);
            return stack.Fail(why);
        }

        lifetime.Moved(made, wasInLevel: false);
        return ScriptResult.Of(context.PushObject(stack, made));
    }
}
