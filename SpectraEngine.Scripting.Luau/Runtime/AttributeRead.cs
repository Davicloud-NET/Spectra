using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// GetAttribute of a node: a keyvalue of the entity it carries, pushed as the
// value its type is to a script. The name is the second value on the stack.
internal static class AttributeRead
{
    public static int Push(ScriptContext context, LuauStack stack, SceneNode node)
    {
        if (!stack.TryGetUtf8(2, out ReadOnlySpan<byte> name))
            return ScriptArguments.Wrong(stack, 2, "GetAttribute", "string", isMethod: true);

        // The name is compared where it lies, so asking makes no string.
        Span<char> room = stackalloc char[NameChars.Room];
        EntityWorld world = context.World;
        if (!NameChars.TryRead(name, room, out ReadOnlySpan<char> wanted) ||
            !AuthoredAttributes.TryRead(node, world.Catalog, wanted, out KeyvalueType type, out ParameterValue value))
        {
            return ScriptResult.Of(stack.PushNil());
        }

        return ScriptResult.Of(value.Kind switch
        {
            ParameterValueKind.Boolean => stack.Push(value.Boolean),
            ParameterValueKind.Number => stack.Push(value.Number),
            ParameterValueKind.Text => stack.Push(value.Text),

            // A keyvalue holds a colour as the engine lights with it.
            ParameterValueKind.Vector when type == KeyvalueType.Color =>
                ColorValue.Push(stack, Color3.FromLinear(ThreeOf(in value))),
            ParameterValueKind.Vector => stack.Push(ThreeOf(in value)),

            // A node that is not in the level is nil, as an empty reference is.
            ParameterValueKind.NodeId => context.PushObject(
                stack, world.Scene.TryFindById(value.NodeId, out SceneNode? found) ? found : null),
            _ => stack.PushNil(),
        });
    }

    private static Vector3 ThreeOf(in ParameterValue value) => new(value.Vector.X, value.Vector.Y, value.Vector.Z);
}
