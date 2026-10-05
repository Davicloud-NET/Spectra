using System;
using System.IO;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
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

    public SpectraEngine.Core.Scene.Scene BuildScene(
        bool withDoorway = true, bool withPart = true, bool withEntities = false)
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

        if (withEntities) AddEntities(scene, wall);

        return scene;
    }

    // A door, the trigger volume wired to it, a relay and a player start. Last
    // in the walk, parts or bare nodes, wearing a material the room already
    // names: they change neither the chunks nor the asset rows.
    private static void AddEntities(SpectraEngine.Core.Scene.Scene scene, MaterialRef wall)
    {
        SceneNode door = Box(scene, "Door", new Vector3(0f, 1.2f, -4.25f), new Vector3(1f, 1.2f, 0.1f), wall);
        door.BrushKind = BrushKind.Part;
        door.Entity = new EntityData("func_door");
        door.Entity.Keyvalues.Add(new("speed", "100"));
        door.Entity.Keyvalues.Add(new("movedir", "0 1 0"));
        door.Entity.Connections.Add(new EntityConnection(
            "OnFullyOpen", "Relay", "Trigger", "", 0f, EntityConnection.Infinite));

        // A trigger volume: touched, never bumped into, never drawn.
        SceneNode trigger = Box(scene, "DoorTrigger", new Vector3(0f, 1.2f, -3f), new Vector3(1f, 1.2f, 1f), wall);
        trigger.BrushKind = BrushKind.Part;
        trigger.CanCollide = false;
        trigger.CanQuery = false;
        trigger.IsRendered = false;
        trigger.Entity = new EntityData("trigger_multiple");
        trigger.Entity.Connections.Add(new EntityConnection(
            "OnStartTouch", "Door", "Open", "", 0f, EntityConnection.Infinite));
        trigger.Entity.Connections.Add(new EntityConnection(
            "OnEndTouch", "Door", "Close", "", 2.5f, EntityConnection.Infinite));

        // A repeated key: both are kept, in order.
        SceneNode relay = scene.Root.CreateChild("Relay");
        relay.Entity = new EntityData("logic_relay");
        relay.Entity.Keyvalues.Add(new("tag", "first"));
        relay.Entity.Keyvalues.Add(new("spawnflags", "1"));
        relay.Entity.Keyvalues.Add(new("tag", "second"));
        relay.Entity.Connections.Add(new EntityConnection("OnTrigger", "Door*", "Lock", "now", 0.5f, 1));

        SceneNode start = scene.Root.CreateChild("Start");
        start.LocalPosition = new Vector3(0f, 1f, 2f);
        start.CanTouch = false;
        start.Entity = new EntityData("info_player_start");
    }

    public string WriteBundle(
        TempProject project,
        string bundleName,
        bool withDoorway = true,
        bool withPart = true,
        bool withEntities = false)
    {
        SpectraEngine.Core.Scene.Scene scene = BuildScene(withDoorway, withPart, withEntities);
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
