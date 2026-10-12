using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The globals every script of a level shares. Its own node, the global
// script, is set for each script when it starts.
[ScriptGlobals]
internal sealed partial class LevelGlobals(DataModel game, SceneNode workspace)
{
    /// <summary>The level. A script asks it for a service: game:GetService("RunService").</summary>
    [ScriptProperty(Name = "game")]
    public DataModel Game { get; } = game;

    /// <summary>The root of the level's tree.</summary>
    [ScriptProperty(Name = "workspace")]
    public SceneNode Workspace { get; } = workspace;
}
