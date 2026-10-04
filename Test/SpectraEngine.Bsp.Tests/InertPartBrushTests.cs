using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A subtractive part brush carves nothing and draws nothing, so the scene
/// counts it.
/// </summary>
public sealed class InertPartBrushTests
{
    private static SceneNode AddBrush(Scene scene, string name, BrushKind kind, BrushOperation operation)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.BrushKind = kind;
        node.Brush = Brush
            .CreateBox(new Vector3(-1f, -1f, -1f), Vector3.One)
            .WithOperation(operation);
        return node;
    }

    [Theory]
    [InlineData(BrushKind.World, BrushOperation.Additive, 0)]
    [InlineData(BrushKind.World, BrushOperation.Subtractive, 0)]
    [InlineData(BrushKind.Part, BrushOperation.Additive, 0)]
    [InlineData(BrushKind.Part, BrushOperation.Subtractive, 1)]
    public void Only_a_subtractive_part_is_inert(BrushKind kind, BrushOperation operation, int expected)
    {
        var scene = new Scene("kinds");
        AddBrush(scene, "B", kind, operation);

        scene.InertPartBrushCount.ShouldBe(expected);
    }

    [Fact]
    public void Converting_a_negative_to_a_part_makes_it_inert_and_back_again()
    {
        // How one comes about in practice: the editor converts a whole selection
        // to parts, and a doorway cut is in it.
        var scene = new Scene("convert");
        SceneNode doorway = AddBrush(scene, "DoorwayCut", BrushKind.World, BrushOperation.Subtractive);
        scene.InertPartBrushCount.ShouldBe(0);

        doorway.BrushKind = BrushKind.Part;
        scene.InertPartBrushCount.ShouldBe(1);

        doorway.BrushKind = BrushKind.World;
        scene.InertPartBrushCount.ShouldBe(0);
    }

    [Fact]
    public void An_inert_part_is_in_neither_the_draw_list_nor_the_carve()
    {
        var scene = new Scene("absent");
        SceneNode node = AddBrush(scene, "Nothing", BrushKind.Part, BrushOperation.Subtractive);

        scene.PartBrushNodes.ShouldNotContain(node);
        node.BrushKind.ShouldBe(BrushKind.Part);
        scene.InertPartBrushCount.ShouldBe(1);
    }
}
