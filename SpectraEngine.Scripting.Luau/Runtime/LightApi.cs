using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>A node that lights the level. Each kind of light is a class of its own.</summary>
// Range is not here: a directional light has none, so each kind that has
// one declares it.
[ScriptClass("Light", Extends = "Instance")]
internal static partial class LightApi
{
    /// <summary>Whether the light shines. One that is switched off lights nothing from the next frame on.</summary>
    [ScriptProperty(Sets = SettableMemberId.Enabled)]
    public static partial bool Enabled(SceneNode node);

    /// <summary>How bright the light is: 0 or more, and the light's colour is multiplied by it. A number below 0 is taken as 0, as Roblox takes it. It is the Intensity the editor shows. Roblox's Brightness is on a scale of its own, so a number from a Roblox place is a first guess here.</summary>
    [ScriptProperty(Sets = SettableMemberId.Brightness)]
    public static partial float Brightness(SceneNode node);

    /// <summary>The light's colour, as a colour picker shows it. A part above 1 makes the light brighter, and none may be below 0.</summary>
    [ScriptProperty(Sets = SettableMemberId.Color)]
    public static partial Color3 Color(SceneNode node);
}
