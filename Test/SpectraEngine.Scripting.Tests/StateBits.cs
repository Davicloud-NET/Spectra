using System.Numerics;
using System.Text;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

// A character's state written out bit for bit, so two runs are held to the
// same bits and a difference reads as the field it is in. A node is named,
// since two levels built alike give their nodes ids of their own.
internal static class StateBits
{
    public static string Of(in CharacterState state, Scene scene)
    {
        var text = new StringBuilder();
        Add(text, "at", state.Position);
        Add(text, "moving", state.Velocity);
        Add(text, "ground", state.GroundNormal);
        Add(text, "origin", state.GroundOrigin);
        text.Append($"grounded={state.Grounded} buttons={state.PrevButtons} air={state.AirTicks} ");
        text.Append($"buffer={state.JumpBufferTicks} suppress={state.GroundSuppressTicks} ");
        text.Append($"stepped={Bits(state.SteppedUpBy)} yaw={Bits(state.Yaw)} on={Name(scene, state.GroundNodeId)} slid=");
        for (int i = 0; i < state.SlideContacts.Count; i++)
            text.Append(Name(scene, state.SlideContacts[i])).Append(',');

        text.Append(" keeps=");
        for (int i = 0; i < CharacterMoverMemory.Capacity; i++)
            text.Append(Bits(state.Memory[i])).Append(',');

        return text.ToString();
    }

    private static void Add(StringBuilder text, string name, Vector3 value) =>
        text.Append($"{name}={Bits(value.X)},{Bits(value.Y)},{Bits(value.Z)} ");

    private static string Bits(float value) => BitConverter.SingleToInt32Bits(value).ToString("x8");

    private static string Name(Scene scene, Guid id) =>
        id == Guid.Empty ? "-" : scene.TryFindById(id, out SceneNode? node) ? node.Name : "gone";
}
