namespace SpectraEngine.Core.Scene;

/// <summary>
/// What <see cref="SceneManager.LoadStartupScene"/> builds when the engine
/// comes up. The demo is the default; the editor shell asks for the baseplate
/// and then opens the project's startup map.
/// </summary>
public enum StartupSceneKind
{
    /// <summary>The authored demo scene, the engine's own smoke fixture.</summary>
    Demo,

    /// <summary>A sun and a ground plate: lit, with a floor to build and walk on.</summary>
    Baseplate,
}
