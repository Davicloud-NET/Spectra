using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

// A project whose one level has the asking script under every kind of node a
// level can hold: world brushes, a cut, parts, lights, entities, a mesh,
// folders and other scripts. Written as the editor writes a project.
internal static class AskedLevel
{
    public const string Bundle = "Maps/Level.smap";

    // A script node whose file nobody wrote. It starts in neither level.
    public const string LostScript = "Scripts/lost.luau";

    private const string Model = "Models/signpost.gltf";

    // Two bytes in UTF-8, by its code point: an escape in source does not
    // survive every editing tool.
    public static readonly string DoorInGerman = $"T{(char)0xFC}r";

    // Every node the asking script sits under and runs. A script says its
    // node's name in front of each line.
    public static readonly string[] Askers =
    [
        "Root", "Floor", "Doorway", "Crate", "Ask", "Door", "Relay", "Start", "Sun", "Spot", "Bulb",
        "Shelf", "Plank", "Props", "Prop", DoorInGerman, "Leaf", "Later", "Rules", "Lamp", "Panel", "Disc",
    ];

    public static TempProject Write()
    {
        var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteMaterials(project);
        project.WriteAsset(AskingScript.Path, AskingScript.Text);
        project.WriteAsset(RequiringScript.Path, RequiringScript.Text);
        project.WriteAsset(RequiringScript.ModulePath, RequiringScript.ModuleText);
        foreach ((string path, string text) in RequiringScript.Modules)
            project.WriteAsset(path, text);
        project.WriteAsset(TaggingScript.Path, TaggingScript.Text);

        project.WriteAsset(TouchingScript.Path, TouchingScript.Text);
        project.WriteAsset(MakingScript.Path, MakingScript.Text);
        project.WriteAsset(BuiltTagsScript.Path, BuiltTagsScript.Text);

        // The prop's model, with what it wears.
        foreach (string path in new[]
                 {
                     Model, "Materials/PostWood.spectramat", "Materials/SignFace.spectramat",
                     "Textures/wall_brick.png", "Textures/dev_grid.png",
                 })
        {
            project.WriteAsset(path, File.ReadAllBytes(ContentRoot.ResolveAbsolute(ContentRoot.Path, path)));
        }

        MapDocument document = MapSceneBinder.FromScene(Build(fixture));

        // A mesh is named in the document: a scene only has one once a
        // renderer has loaded its model.
        Find(document.Nodes, "Prop").Mesh = new MapMeshSource { Model = Model };

        string bundle = Path.Combine(project.Layout.MapsPath, Path.GetFileName(Bundle));
        Directory.CreateDirectory(bundle);
        MapBundle.Save(bundle, document);
        return project;
    }

    private static Scene Build(MapFixture fixture)
    {
        Scene scene = fixture.BuildScene(withEntities: true, withLights: true);
        SceneNode root = scene.Root;

        // A folder that holds a world brush: neither moves while a level plays.
        SceneNode shelf = root.CreateChild("Shelf");
        SceneNode plank = shelf.CreateChild("Plank");
        plank.LocalPosition = new Vector3(4f, 1f, 0f);
        plank.Brush = Brush.CreateBox(new Vector3(-1f, -0.05f, -0.25f), new Vector3(1f, 0.05f, 0.25f));

        SceneNode props = root.CreateChild("Props");
        props.LocalPosition = new Vector3(-4f, 0f, 2f);
        SceneNode prop = props.CreateChild("Prop");
        prop.LocalPosition = new Vector3(0.5f, 0f, 0.25f);

        // A turned folder with a part in it, so a world frame is a product.
        SceneNode door = root.CreateChild(DoorInGerman);
        door.LocalPosition = new Vector3(5f, 0.5f, 5f);
        door.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.5f, 0.1f, 0f);
        SceneNode leaf = door.CreateChild("Leaf");
        leaf.LocalPosition = new Vector3(1f, 0f, 0f);
        leaf.BrushKind = BrushKind.Part;
        leaf.Brush = Brush.CreateBox(new Vector3(-0.5f, -0.5f, -0.05f), new Vector3(0.5f, 0.5f, 0.05f));

        // Neither runs. Each is still a node a script can sit under.
        SceneNode later = root.CreateChild("Later");
        later.Script = new Script(AskingScript.Path) { IsEnabled = false };
        SceneNode rules = root.CreateChild("Rules");
        rules.Script = new Script(AskingScript.Path) { IsModule = true };

        root.CreateChild("Lost").Script = new Script(LostScript);

        // A module node, and under it the script that asks for it and for
        // modules no node names.
        SceneNode answers = root.CreateChild(RequiringScript.ModuleNode);
        answers.Script = new Script(RequiringScript.ModulePath) { IsModule = true };
        answers.CreateChild("Requires").Script = new Script(RequiringScript.Path);

        foreach (string name in Askers.Where(name => name is not ("Root" or "Ask")))
            Ask(Named(scene, name));

        // One on the root, and one under another: a script's parent is a script.
        Ask(root);
        Ask(Named(scene, "Crate").Children.Single(child => child.Script is not null));
        AddWhatThePlayerTouches(root, Named(scene, "Start").LocalPosition);

        // The tags and attributes the level is built with, and who reads them.
        BuiltTagsScript.AddTo(scene);

        // Last under the root, so it starts after every other script.
        root.CreateChild(TaggingScript.Node).Script = new Script(TaggingScript.Path);

        // After it, and it waits before it makes anything.
        root.CreateChild(MakingScript.Node).Script = new Script(MakingScript.Path);
        return scene;
    }

    // A pad under the player's start and a zone around it, with the script
    // that listens to both. No asking script sits under either, so nothing
    // moves them or switches them off.
    private static void AddWhatThePlayerTouches(SceneNode root, Vector3 start)
    {
        SceneNode pad = root.CreateChild(TouchingScript.Pad);
        pad.LocalPosition = new Vector3(start.X, 0.05f, start.Z);
        pad.BrushKind = BrushKind.Part;
        pad.Brush = Brush.CreateBox(new Vector3(-0.5f, -0.05f, -0.5f), new Vector3(0.5f, 0.05f, 0.5f));
        pad.CreateChild("Touches").Script = new Script(TouchingScript.Path);

        SceneNode zone = root.CreateChild(TouchingScript.Zone);
        zone.LocalPosition = start;
        zone.BrushKind = BrushKind.Part;
        zone.Brush = Brush.CreateBox(new Vector3(-0.75f, -1f, -0.75f), new Vector3(0.75f, 1f, 0.75f));
        zone.CanCollide = false;
        zone.CanQuery = false;
        zone.IsRendered = false;
    }

    private static void Ask(SceneNode under) =>
        under.CreateChild("Ask").Script = new Script(AskingScript.Path);

    private static SceneNode Named(Scene scene, string name) =>
        scene.Root.Traverse().Single(node => node.Name == name);

    private static MapNode Find(List<MapNode> nodes, string name) =>
        TryFind(nodes, name) ?? throw new InvalidOperationException($"The level has no node named '{name}'.");

    private static MapNode? TryFind(List<MapNode> nodes, string name)
    {
        foreach (MapNode node in nodes)
        {
            if (node.Name == name) return node;
            if (TryFind(node.Children, name) is { } below) return below;
        }

        return null;
    }
}
