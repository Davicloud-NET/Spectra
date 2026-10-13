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

    /// <summary>Whether the part notices what touches it. A trigger with it off stops firing. Only a part an entity senses touch with reads it: on any other part and on a mesh it changes nothing yet.</summary>
    [ScriptProperty(Sets = SettableMemberId.CanTouch)]
    public static partial bool CanTouch(SceneNode node);

    /// <summary>Whether the part is drawn. With it off the part is not drawn and casts no shadow, and is as solid as before. What is below it in the tree is drawn as before. Roblox has no such switch and hides a part with Transparency.</summary>
    [ScriptProperty(Sets = SettableMemberId.Visible)]
    public static partial bool Visible(SceneNode node);
}
