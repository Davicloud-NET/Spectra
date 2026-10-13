using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>A light that shines from a point in every direction.</summary>
[ScriptClass("PointLight", Extends = "Light")]
internal static partial class PointLightApi
{
    /// <summary>How far the light reaches, in units: nothing past it is lit. It is above 0: a light cannot have no reach, so 0 and less are an error, where Roblox takes them as 0. A unit is a metre and Roblox counts in studs, so Roblox's 16 is about 4.5 here.</summary>
    [ScriptProperty(Sets = SettableMemberId.Range)]
    public static partial float Range(SceneNode node);
}
