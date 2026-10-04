using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraEngine.Core.Scene;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The outline pass: a queued mesh gets a line round its silhouette, outside
/// it, and nothing else in the frame changes.
/// </summary>
[Collection(GlRendererCollection.Name)]
public sealed class OutlineGlTests
{
    private const int Size = 64;

    private readonly GlRendererFixture _fixture;

    public OutlineGlTests(GlRendererFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("Deferred")]
    [InlineData("Forward")]
    public void A_queued_mesh_gets_a_line_just_outside_its_silhouette(string pipeline)
    {
        Scene scene = BuildCube(out SceneNode cube, occluded: false);

        bool[] row = RenderRow(scene, pipeline, cube);

        int centre = Size / 2;
        row[centre].ShouldBeFalse("the outline is drawn outside the silhouette, never over the mesh");
        row[1].ShouldBeFalse("a pixel far from the mesh keeps the sky");

        int left = Array.IndexOf(row, true);
        int right = Array.LastIndexOf(row, true);
        left.ShouldBeInRange(2, centre - 2, "an outline run left of the mesh");
        right.ShouldBeInRange(centre + 2, Size - 3, "an outline run right of the mesh");
    }

    [Theory]
    [InlineData("Deferred")]
    [InlineData("Forward")]
    public void Nothing_queued_draws_no_outline(string pipeline)
    {
        // The control: without it, the test above could be passing on the
        // mesh's own colour.
        Scene scene = BuildCube(out _, occluded: false);

        RenderRow(scene, pipeline, outlined: null).ShouldAllBe(lit => !lit);
    }

    [Fact]
    public void The_outline_shows_through_whatever_is_in_front()
    {
        // The mask is drawn with nothing else in it, so a selected object
        // behind a wall is still outlined whole.
        Scene scene = BuildCube(out SceneNode cube, occluded: true);

        bool[] row = RenderRow(scene, "Deferred", cube);

        row.Count(lit => lit).ShouldBeGreaterThanOrEqualTo(2);
    }

    // The middle row of the resolved frame: true where a pixel is the outline's orange.
    private bool[] RenderRow(Scene scene, string pipeline, SceneNode? outlined)
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        string restore = renderer.CurrentPipelineName;
        RenderTarget output = renderer.CreateRenderTarget(new RenderTargetDesc(Size, Size));
        var view = new RenderView();

        try
        {
            renderer.TrySelectPipeline(pipeline).ShouldBeTrue();
            renderer.SupportsOutlines.ShouldBeTrue();

            renderer.Outlines.Clear();
            if (outlined?.MeshRenderer is { } shape)
                renderer.Outlines.Add(shape.Mesh, outlined.WorldMatrix, OutlineGroup.Selected);

            scene.BuildRenderView(scene.Camera, view);
            renderer.Render(scene, view, 1.0 / 60.0);

            // The frame's own resolve went to the window. Run it again into a
            // target that can be read.
            renderer.ResolveForTest(renderer.SceneTarget!.ColorTexture!, output, scene);

            var row = new bool[Size];
            for (int x = 0; x < Size; x++)
            {
                (byte r, byte g, byte b, _) = renderer.ReadTargetPixel(output, x, Size / 2);

                // Orange: far redder than blue. The sky is blue and both meshes are grey.
                row[x] = r > 170 && b < 90 && r - g > 60;
            }

            return row;
        }
        finally
        {
            renderer.Outlines.Clear();
            renderer.DestroyRenderTarget(output);
            while (renderer.CurrentPipelineName != restore) renderer.NextPipeline();
        }
    }

    // A grey cube in the middle of the view, and optionally a grey wall
    // between it and the camera.
    private Scene BuildCube(out SceneNode cube, bool occluded)
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        var scene = new Scene("outline");
        scene.Camera.Position = new Vector3(0f, 0f, 4f);
        scene.Camera.LookAt(Vector3.Zero);

        var (vertices, indices) = Primitives.Cube();
        Mesh mesh = renderer.CreateMesh(vertices, indices, VertexAttribute.StandardLayout);

        Texture white = renderer.CreateTexture(
            [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);

        var material = new Material(renderer.DefaultShader);
        material
            .SetVector3("uBaseColor", new Vector3(0.5f, 0.5f, 0.5f))
            .SetFloat("uRoughness", 0.9f)
            .SetFloat("uMetallic", 0f)
            .SetFloat("uAmbientOcclusion", 1f)
            .SetVector3("uEmissive", Vector3.Zero)
            .SetFloat("uShadingModel", 0f)
            .SetTexture("uDiffuse", 0, white);

        cube = scene.Root.CreateChild("Cube");
        cube.MeshRenderer = new MeshRenderer(mesh, material);

        if (occluded)
        {
            SceneNode wall = scene.Root.CreateChild("Wall");
            wall.LocalTransform = new Transform
            {
                Position = new Vector3(0f, 0f, 1.5f),
                Rotation = Quaternion.Identity,
                Scale = new Vector3(8f, 8f, 0.2f),
            };
            wall.MeshRenderer = new MeshRenderer(mesh, material);
        }

        SceneNode lamp = scene.Root.CreateChild("Lamp");
        lamp.LocalPosition = new Vector3(0f, 0f, 3f);
        lamp.Light = new Light
        {
            Kind = LightKind.Point,
            Color = Vector3.One,
            Intensity = 20f,
            Range = 8f,
        };

        return scene;
    }
}
