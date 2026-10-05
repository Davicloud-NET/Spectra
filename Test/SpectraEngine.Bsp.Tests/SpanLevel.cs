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

    public Scene Scene { get; } = new("SpanLevel");

    public SceneNode Box(string name, Vector3 center, Vector3 half, MaterialRef material = default)
    {
        SceneNode node = Scene.Root.CreateChild(name);
        node.LocalPosition = origin + center;
        node.Brush = Brush.CreateBox(-half, half, material);
        return node;
    }

    public SceneNode Cut(string name, Vector3 center, Vector3 half, MaterialRef material = default)
    {
        SceneNode node = Scene.Root.CreateChild(name);
        node.LocalPosition = origin + center;
        node.Brush = Brush.CreateBox(-half, half, material).WithOperation(BrushOperation.Subtractive);
        return node;
    }

    public SceneNode Part(string name, Vector3 center, Vector3 half, MaterialRef material = default)
    {
        SceneNode node = Scene.Root.CreateChild(name);
        node.LocalPosition = origin + center;

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(-half, half, material);
        return node;
    }

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
        SceneNode wall = Box(name, new Vector3(0f, 1.5f, -4.25f), new Vector3(6f, 1.5f, 0.25f), Brick);
        wall.Brush = wall.Brush!.WithFaceMaterial(PlusZ, Plaster).WithFaceMaterial(PlusX, Tile);
        return wall;
    }

    // A doorway through that wall, flush with both faces and with its base:
    // x from -1 to 1, up to y = 2.4.
    public SceneNode Doorway(string name = "Doorway") =>
        Cut(name, new Vector3(0f, 1.2f, -4.25f), new Vector3(1f, 1.2f, 0.25f));

    // Brush.CreateBox's plane order.
    public const int PlusX = 0;
    public const int PlusZ = 4;
}
