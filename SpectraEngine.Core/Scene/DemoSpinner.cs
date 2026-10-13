using System;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// One of the demo level's scripts: a small part in the start room that a
/// <c>.luau</c> file turns while the level plays.
/// </summary>
// Not in DemoPlayArea, which is the level that needs no code and is baked by
// tests that allow no warning. A baked map leaves scripts out and says so.
public static class DemoSpinner
{
    /// <summary>The part that turns.</summary>
    public const string PartName = "Spinner";

    /// <summary>The script node under it.</summary>
    public const string ScriptName = "SpinnerScript";

    /// <summary>The script's file, as a content path.</summary>
    public const string ScriptPath = "Scripts/spinner.luau";

    /// <summary>
    /// Where the part floats: at eye height beside the start room's door, in
    /// view of someone who has just started.
    /// </summary>
    public static readonly Vector3 Position = new(129f, 1.5f, 2.5f);

    private const float HalfSize = 0.2f;

    /// <summary>Adds the part and its script to a scene and returns the part.</summary>
    public static SceneNode Build(Scene scene, MaterialRef material)
    {
        ArgumentNullException.ThrowIfNull(scene);

        SceneNode part = scene.Root.CreateChild(PartName);
        part.LocalPosition = Position;

        // Kind before brush, or the node is briefly a world brush.
        part.BrushKind = BrushKind.Part;
        part.Brush = Brush.CreateBox(new Vector3(-HalfSize), new Vector3(HalfSize), material);

        // Only to look at: it stops nobody and no ray or sound finds it.
        part.CanCollide = false;
        part.CanQuery = false;

        part.CreateChild(ScriptName).Script = new Script(ScriptPath);
        return part;
    }
}
