using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// A keyvalue of the entity a node carries, as the value its type is to a
// script. That is what GetAttribute gives for a name no script has set.
internal static class AttributeRead
{
    // False when the node has no keyvalue of that name. A reference to a
    // node comes back in `reference`, since no value a script sets is one.
    // No value and no reference is nil: an empty reference, or one to a node
    // that is not in the level.
    public static bool TryRead(
        ScriptContext context, SceneNode node, ReadOnlySpan<char> name, out MemberValue value, out SceneNode? reference)
    {
        value = MemberValue.None;
        reference = null;

        EntityWorld world = context.World;
        if (!AuthoredAttributes.TryRead(node, world.Catalog, name, out KeyvalueType type, out ParameterValue built))
            return false;

        switch (built.Kind)
        {
            case ParameterValueKind.Boolean:
                value = MemberValue.Of(built.Boolean);
                break;
            case ParameterValueKind.Number:
                value = MemberValue.Of(built.Number);
                break;
            case ParameterValueKind.Text:
                value = MemberValue.Of(built.Text);
                break;

            // A keyvalue holds a colour as the engine lights with it.
            case ParameterValueKind.Vector when type == KeyvalueType.Color:
                value = MemberValue.Of(Color3.FromLinear(ThreeOf(in built)));
                break;
            case ParameterValueKind.Vector:
                value = MemberValue.Of(ThreeOf(in built));
                break;
            case ParameterValueKind.NodeId:
                world.Scene.TryFindById(built.NodeId, out reference);
                break;
        }

        return true;
    }

    // Pushes what TryRead gave. False: it could not be pushed, and an error
    // value is on top instead.
    public static bool Push(ScriptContext context, LuauStack stack, in MemberValue value, SceneNode? reference) =>
        reference is null ? AttributeValues.Push(stack, in value) : context.PushObject(stack, reference);

    private static Vector3 ThreeOf(in ParameterValue value) => new(value.Vector.X, value.Vector.Y, value.Vector.Z);
}
