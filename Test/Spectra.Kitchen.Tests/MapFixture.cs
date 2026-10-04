using System;
using System.IO;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

// Map bundles for the bake tests, written as the editor writes them.
// The room is the flush-coplanar-cut case: a doorway cut flush through its
// wall's base, on a floor whose top is that same plane. Don't nudge either.
// Two materials so submesh order is testable; a part brush so BRSH is always
// present. Material paths are unique per instance because MaterialRegistry is
// process-global and append-only.
internal sealed class MapFixture
{
    private MapFixture(string wall, string floor)
    {
        WallMaterial = wall;
        FloorMaterial = floor;
    }

    public string WallMaterial { get; }

    // Worn by the floor and the part.
    public string FloorMaterial { get; }

    public static MapFixture Fresh([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        string stamp = Guid.NewGuid().ToString("N")[..8];
        return new MapFixture(
            $"Materials/{caller}_{stamp}_wall.spectramat",
            $"Materials/{caller}_{stamp}_floor.spectramat");
    }

    public SpectraEngine.Core.Scene.Scene BuildScene(bool withDoorway = true, bool withPart = true)
    {
        var scene = new SpectraEngine.Core.Scene.Scene("BakeRoom");

        MaterialRef wall = MaterialRegistry.Intern(WallMaterial);
        MaterialRef floor = MaterialRegistry.Intern(FloorMaterial);

        // Floor top at y = 0: the wall's base and the plane the cut reaches.
        SceneNode ground = Box(scene, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(6f, 0.5f, 6f), floor);

        // One face retextured so the floor's cell has two materials.
        ground.Brush = ground.Brush!.WithFaceMaterial(0, wall);
        Box(scene, "Wall", new Vector3(0f, 1.5f, -4.25f), new Vector3(6f, 1.5f, 0.25f), wall);
        Box(scene, "BackWall", new Vector3(0f, 1.5f, 4.25f), new Vector3(6f, 1.5f, 0.25f), wall);

        if (withDoorway)
        {
            SceneNode cut = Box(
                scene, "Doorway", new Vector3(0f, 1.2f, -4.25f), new Vector3(1f, 1.2f, 0.25f), wall);

            cut.Brush = cut.Brush!.WithOperation(BrushOperation.Subtractive);
        }

        if (withPart)
        {
            SceneNode part = Box(scene, "Crate", new Vector3(2f, 0.5f, 2f), new Vector3(0.5f), floor);
            part.BrushKind = BrushKind.Part;
        }

        return scene;
    }

    public string WriteBundle(TempProject project, string bundleName, bool withDoorway = true, bool withPart = true)
    {
        SpectraEngine.Core.Scene.Scene scene = BuildScene(withDoorway, withPart);
        return WriteBundle(project, bundleName, scene);
    }

    public static string WriteBundle(
        TempProject project, string bundleName, SpectraEngine.Core.Scene.Scene scene)
    {
        string bundle = Path.Combine(project.Layout.MapsPath, bundleName);
        Directory.CreateDirectory(bundle);
        MapBundle.Save(bundle, MapSceneBinder.FromScene(scene));
        return bundle;
    }

    // So a cooked pack carries the materials the map names.
    public void WriteMaterials(TempProject project)
    {
        foreach (string path in new[] { WallMaterial, FloorMaterial })
            project.WriteAsset(path, "shader = lit\n");
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
}
