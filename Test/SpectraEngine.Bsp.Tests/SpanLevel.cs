using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// A level for the span query: boxes placed by centre and half size, as world
// brushes, cuts or parts. Origin moves the whole level and every trace with
// it, so one test can run near the world origin and far from it.
internal sealed class SpanLevel(Vector3 origin = default)
{
    public static readonly MaterialRef Brick = MaterialRegistry.Intern("Materials/span_brick.spectramat");
    public static readonly MaterialRef Plaster = MaterialRegistry.Intern("Materials/span_plaster.spectramat");
    public static readonly MaterialRef Tile = MaterialRegistry.Intern("Materials/span_tile.spectramat");
    public static readonly MaterialRef Wood = MaterialRegistry.Intern("Materials/span_wood.spectramat");

    // Brush.CreateBox's plane order.
    private const int PlusX = 0;
    private const int PlusZ = 4;

    public Scene Scene { get; } = new("SpanLevel");

    public SceneNode Box(string name, Vector3 center, Vector3 half, MaterialRef material = default) =>
        Place(name, center, BrushKind.World, Brush.CreateBox(-half, half, material));

    public SceneNode Cut(string name, Vector3 center, Vector3 half, MaterialRef material = default) =>
        Place(name, center, BrushKind.World, Subtractive(half, material));

    public SceneNode Part(string name, Vector3 center, Vector3 half, MaterialRef material = default) =>
        Place(name, center, BrushKind.Part, Brush.CreateBox(-half, half, material));

    // A part that carves nothing: a mistake a level can hold.
    public SceneNode SubtractivePart(string name, Vector3 center, Vector3 half) =>
        Place(name, center, BrushKind.Part, Subtractive(half, default));

    public SpanLevel Compile()
    {
        Scene.RebuildStaticWorld(new FakeRenderer());
        return this;
    }

    public SolidSpan[] Trace(Vector3 from, Vector3 to)
    {
        var spans = new SolidSpan[32];
        int count = Scene.TraceSolidSpans(origin + from, origin + to, spans, out bool truncated);
        truncated.ShouldBeFalse();
        return spans[..count];
    }

    public SolidSpan[] Trace(Vector3 from, Vector3 to, in SceneQueryFilter filter)
    {
        var spans = new SolidSpan[32];
        int count = Scene.TraceSolidSpans(origin + from, origin + to, in filter, spans, out bool truncated);
        truncated.ShouldBeFalse();
        return spans[..count];
    }

    // The wall every span test crosses: z from -4.5 to -4, x from -6 to 6 and
    // y from 0 to 3. Brick, with plaster on the face toward +z and tile on the
    // end toward +x.
    public SceneNode Wall(string name = "Wall")
    {
        var half = new Vector3(6f, 1.5f, 0.25f);
        Brush brush = Brush.CreateBox(-half, half, Brick)
            .WithFaceMaterial(PlusZ, Plaster)
            .WithFaceMaterial(PlusX, Tile);

        return Place(name, new Vector3(0f, 1.5f, -4.25f), BrushKind.World, brush);
    }

    // A doorway through that wall, flush with both faces and with its base:
    // x from -1 to 1, up to y = 2.4. reveal is what its jambs and head wear.
    public SceneNode Doorway(MaterialRef reveal = default) =>
        Cut("Doorway", new Vector3(0f, 1.2f, -4.25f), new Vector3(1f, 1.2f, 0.25f), reveal);

    private SceneNode Place(string name, Vector3 center, BrushKind kind, Brush brush)
    {
        SceneNode node = Scene.Root.CreateChild(name);
        node.LocalPosition = origin + center;

        // Kind before brush, or a part is briefly a world brush.
        node.BrushKind = kind;
        node.Brush = brush;
        return node;
    }

    private static Brush Subtractive(Vector3 half, MaterialRef material) =>
        Brush.CreateBox(-half, half, material).WithOperation(BrushOperation.Subtractive);
}
