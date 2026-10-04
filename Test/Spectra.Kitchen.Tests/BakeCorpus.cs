using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

// The maps the bake oracle runs over. Each one covers a file shape the others
// cannot reach. Material paths are unique per instance: MaterialRegistry is
// process-global and append-only.
internal sealed class BakeCorpus
{
    // Doorway cut flush through its wall's base. Same room as MapFixture.
    public const string FlushCoplanarRoom = "flush-coplanar-room";

    // A tunnel, a sealed pocket, a cut across a cell boundary and a window
    // through a second brush.
    public const string Cavities = "cavities";

    // One cell, seven materials, six interned in reverse of the node walk, plus
    // a brush with no material. Makes the bake's submesh sort a real permutation.
    public const string Palette = "palette";

    // Many cells, one material: cells that carry a tree and no mesh. Blind to
    // submesh ordering on purpose, it is the control for the other fixtures.
    public const string Sprawl = "sprawl";

    public static readonly string[] Names = [FlushCoplanarRoom, Cavities, Palette, Sprawl];

    private readonly string _name;
    private readonly string _stamp;
    private readonly MapFixture? _room;

    private BakeCorpus(string name, string stamp, MapFixture? room)
    {
        _name = name;
        _stamp = stamp;
        _room = room;
    }

    public static BakeCorpus Fresh(string name)
    {
        string stamp = Guid.NewGuid().ToString("N")[..8];
        return new BakeCorpus(
            name, stamp, name == FlushCoplanarRoom ? MapFixture.Fresh(name) : null);
    }

    public SpectraEngine.Core.Scene.Scene BuildScene() => _name switch
    {
        FlushCoplanarRoom => _room!.BuildScene(),
        Cavities => BuildCavities(),
        Palette => BuildPalette(),
        Sprawl => BuildSprawl(),
        _ => throw new ArgumentOutOfRangeException(nameof(_name), _name, "No such fixture in the corpus."),
    };

    // Negative control: if the cuts removed nothing, this compiles the same.
    public SpectraEngine.Core.Scene.Scene BuildSceneWithoutCuts()
    {
        SpectraEngine.Core.Scene.Scene scene = BuildScene();
        var doomed = new List<SceneNode>();

        foreach (SceneNode node in scene.Root.Traverse())
        {
            if (node.Brush is { Operation: BrushOperation.Subtractive }) doomed.Add(node);
        }

        foreach (SceneNode node in doomed) node.Parent?.RemoveChild(node);
        return scene;
    }

    // No .spectramat is written: MapRule never resolves a material.
    private string Path(int index) => $"Materials/{_name}_{_stamp}_{index}.spectramat";

    private SpectraEngine.Core.Scene.Scene BuildCavities()
    {
        var scene = new SpectraEngine.Core.Scene.Scene("BakeCavities");

        MaterialRef slabFace = MaterialRegistry.Intern(Path(0));
        MaterialRef wallFace = MaterialRegistry.Intern(Path(1));
        MaterialRef cutFace = MaterialRegistry.Intern(Path(2));

        // Centred on both cell planes so the cuts land on either side of a boundary.
        Box(scene, "Slab", new Vector3(0f, 0f, 0f), new Vector3(10f, 2f, 10f), slabFace);

        // Reaches past the slab in x: open at both ends.
        Cut(scene, "Tunnel", new Vector3(0f, 0f, 0f), new Vector3(12f, 0.75f, 1.5f), cutFace);

        // Sealed: touches no face of the slab.
        Cut(scene, "Pocket", new Vector3(5f, 0f, 5f), new Vector3(1.5f, 0.75f, 1.5f), cutFace);

        // Straddles the x = 0 cell plane.
        Cut(scene, "SplitPocket", new Vector3(0f, 0.5f, -6f), new Vector3(1f, 0.6f, 1f), cutFace);

        // A window: touches neither the wall's top nor its bottom.
        Box(scene, "Wall", new Vector3(0f, 3.5f, 6f), new Vector3(10f, 1.5f, 0.5f), wallFace);
        Cut(scene, "Window", new Vector3(0f, 3.5f, 6f), new Vector3(1.2f, 0.6f, 0.75f), cutFace);

        return scene;
    }

    private SpectraEngine.Core.Scene.Scene BuildPalette()
    {
        var scene = new SpectraEngine.Core.Scene.Scene("BakePalette");

        // Interned backwards, so ids descend while asset rows ascend.
        var faces = new MaterialRef[6];
        for (int i = 5; i >= 0; i--) faces[i] = MaterialRegistry.Intern(Path(i));

        // Inside cell (0,0,0), so all seven submeshes share one directory.
        SceneNode painted = Box(
            scene, "Painted", new Vector3(8f, 8f, 8f), new Vector3(3f), faces[0]);

        for (int i = 1; i < faces.Length; i++)
            painted.Brush = painted.Brush!.WithFaceMaterial(i, faces[i]);

        // No material, same cell: NoAssetIndex sorts last among real rows.
        SceneNode bare = scene.Root.CreateChild("Bare");
        bare.LocalPosition = new Vector3(16f, 8f, 8f);
        bare.Brush = Brush.CreateBox(new Vector3(-3f), new Vector3(3f));

        return scene;
    }

    private SpectraEngine.Core.Scene.Scene BuildSprawl()
    {
        var scene = new SpectraEngine.Core.Scene.Scene("BakeSprawl");

        MaterialRef only = MaterialRegistry.Intern(Path(0));

        // Five cells wide, two deep, owned by one. The rest get a tree and no mesh.
        Box(scene, "Concourse", new Vector3(48f, -1f, 0f), new Vector3(60f, 1f, 8f), only);

        // Straddles the x = 32 cell face.
        Box(scene, "OnTheFace", new Vector3(32f, 4f, 0f), new Vector3(2f), only);

        // Straddles a cell corner: x = 64, y = 0 and z = 32, eight cells.
        Box(scene, "OnTheCorner", new Vector3(64f, 0f, 32f), new Vector3(3f), only);

        // Far off and negative on two axes.
        Box(scene, "Island", new Vector3(-100f, 0f, -100f), new Vector3(4f), only);

        // The only brush not axis-aligned, so its vertices are not round numbers.
        SceneNode tilted = Box(scene, "Tilted", new Vector3(20f, 2f, 6f), new Vector3(4f, 2f, 3f), only);
        tilted.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.6f, 0.25f, -0.4f);

        return scene;
    }

    private static SceneNode Box(
        SpectraEngine.Core.Scene.Scene scene,
        string name,
        Vector3 center,
        Vector3 half,
        MaterialRef material)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = center;
        node.Brush = Brush.CreateBox(-half, half, material);
        return node;
    }

    private static SceneNode Cut(
        SpectraEngine.Core.Scene.Scene scene,
        string name,
        Vector3 center,
        Vector3 half,
        MaterialRef material)
    {
        SceneNode node = Box(scene, name, center, half, material);
        node.Brush = node.Brush!.WithOperation(BrushOperation.Subtractive);
        return node;
    }
}
