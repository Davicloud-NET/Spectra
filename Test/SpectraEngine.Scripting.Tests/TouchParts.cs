using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

// What the tests of touch build a level from. The player starts a unit above
// the origin, falls onto what is under it and walks along +X at yaw zero.
internal static class TouchParts
{
    // A script that says when the part it sits under is touched and left,
    // and whether it was handed the character.
    public const string Reporter = """
        local character = game:GetService("Players").LocalPlayer.Character
        local part = script.Parent
        part.Touched:Connect(function(hit) print(part.Name .. " touched", hit == character) end)
        part.TouchEnded:Connect(function(hit) print(part.Name .. " left", hit == character) end)
        """;

    // A part brush box of any size, solid or not.
    public static SceneNode Box(
        ScriptLevel level, string name, Vector3 center, Vector3 half, bool isSolid = true, SceneNode? parent = null)
    {
        SceneNode node = (parent ?? level.Scene.Root).CreateChild(name);
        node.LocalPosition = center;

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(-half, half);
        node.CanCollide = isSolid;
        return node;
    }

    // A slab on the ground, a tenth of a unit high: low enough to step onto.
    public static SceneNode Pad(ScriptLevel level, string name, float x, float halfWidth = 1f) =>
        Box(level, name, new Vector3(x, 0.05f, 0f), new Vector3(halfWidth, 0.05f, 1f));

    // A wall across the player's way, or a zone to walk through.
    public static SceneNode Wall(ScriptLevel level, string name, float x, bool isSolid) =>
        Box(level, name, new Vector3(x, 1f, 0f), new Vector3(0.5f, 1f, 2f), isSolid);
}
