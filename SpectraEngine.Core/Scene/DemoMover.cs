using System;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// The demo level's second script: it has a mover written in Luau move the
/// player, in place of the engine's built-in one.
/// </summary>
// Not in DemoPlayArea, as the spinner is not: that level needs no code. A
// host that runs no scripts, or a mover that fails, leaves the player to
// the built-in mover.
public static class DemoMover
{
    /// <summary>The script node, under the level's root.</summary>
    public const string ScriptName = "PlayerMover";

    /// <summary>The script that installs the mover, as a content path.</summary>
    public const string ScriptPath = "Scripts/player_mover.luau";

    /// <summary>The mover it installs, a module the script asks for.</summary>
    public const string MoverPath = "Scripts/movers/walk.luau";

    /// <summary>Adds the script to a scene and returns its node.</summary>
    public static SceneNode Build(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        SceneNode node = scene.Root.CreateChild(ScriptName);
        node.Script = new Script(ScriptPath);
        return node;
    }
}
