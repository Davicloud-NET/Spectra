using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>A node with a shape in the level: a part or a mesh.</summary>
// A world brush is a part too, and refuses a write to any of these: it is
// fused into the level while the level plays.
[ScriptClass("BasePart", Extends = "Instance")]
internal static partial class BasePartApi
{
    /// <summary>Whether the part is solid. With it off the player walks through, from the next tick on. A mesh stops nothing yet, whatever this says.</summary>
    [ScriptProperty(Sets = SettableMemberId.CanCollide)]
    public static partial bool CanCollide(SceneNode node);

    /// <summary>Whether a ray finds the part, such as the one the player uses a button with. It does not follow CanCollide: unlike in Roblox, a solid part can be hidden from rays.</summary>
    [ScriptProperty(Sets = SettableMemberId.CanQuery)]
    public static partial bool CanQuery(SceneNode node);

    /// <summary>Whether the part notices what touches it. With it off a trigger stops firing and Touched does not fire, and a touch that is going on ends on the next tick.</summary>
    [ScriptProperty(Sets = SettableMemberId.CanTouch)]
    public static partial bool CanTouch(SceneNode node);

    /// <summary>Whether the part is drawn. With it off the part is not drawn and casts no shadow, and is as solid as before. What is below it in the tree is drawn as before. Roblox has no such switch and hides a part with Transparency.</summary>
    [ScriptProperty(Sets = SettableMemberId.Visible)]
    public static partial bool Visible(SceneNode node);

    /// <summary>Fires once when the player starts to touch the part: stands on it, pushes against it, or walks into one that is not solid. The function gets the player's character, the one Players.LocalPlayer.Character gives. Only the player touches, where in Roblox every part does. A mesh has no shape of its own yet and is touched by the box around it. A baked map does not load a mesh yet, so nothing touches one there. A world brush cannot tell what touches it, and listening to one is an error.</summary>
    [ScriptSignal("character: Character")]
    public static ScriptSignal Touched(ScriptContext context, SceneNode node) =>
        Touch(context, node, TouchSignal.Touched);

    /// <summary>Fires once when the player stops touching the part, with the player's character. A touch also ends when CanTouch is switched off, when the part leaves the level and when the player does. A character that has left is handed over all the same, and reading a member of it is an error.</summary>
    [ScriptSignal("character: Character")]
    public static ScriptSignal TouchEnded(ScriptContext context, SceneNode node) =>
        Touch(context, node, TouchSignal.TouchEnded);

    // One signal for each part and name, so a script reads the same object
    // every time.
    private static ScriptSignal Touch(ScriptContext context, SceneNode node, string name) =>
        (context.Signals ?? throw new InvalidOperationException("The level's signals are not set up.")).OfTouch(node, name);
}
