using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using SoundCornersSpike.Probes;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SoundCornersSpike.Worlds;

// The demo's course as the engine authors it: DemoPlayArea into a scene, the
// world brushes carved by CsgWorld, the parts and the sounds read off the nodes.
internal sealed class DemoWorld
{
    private DemoWorld(CsgWorld world, PartSet parts, List<DemoSound> sounds, int brushes)
    {
        World = world;
        Parts = parts;
        Sounds = sounds;
        BrushCount = brushes;
    }

    public CsgWorld World { get; }

    public PartSet Parts { get; }

    public List<DemoSound> Sounds { get; }

    public int BrushCount { get; }

    // How far the start room's door slides to be fully open: its own width.
    public const float DoorTravel = 1.4f;

    /// <param name="doorOpen">How far the start room's door has slid.</param>
    /// <param name="roofed">Put a lid on the start room. The demo's has none.</param>
    public static DemoWorld Build(float doorOpen = 0f, bool roofed = false)
    {
        var scene = new Scene("SoundCornersSpike");
        DemoPlayArea.Build(scene, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default);

        if (roofed)
        {
            // Over the room and its four walls, standing on their tops.
            SceneNode roof = scene.Root.CreateChild("Spike.StartRoof");
            roof.LocalPosition = new Vector3(126f, 2.75f, 0f);
            roof.Brush = Brush.CreateBox(new Vector3(-5f, -0.25f, -5f), new Vector3(5f, 0.25f, 5f));
        }

        IReadOnlyList<BrushPlacement> placements = scene.CaptureStaticWorldPlacements(out string? defect)
            ?? throw new InvalidOperationException(defect);

        // A set has no order of its own. Sorted, so every run sees the same list.
        var partNodes = new List<SceneNode>(scene.PartBrushNodes);
        partNodes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var parts = new PartSet();
        var partIndex = new Dictionary<SceneNode, int>();
        foreach (SceneNode node in partNodes)
        {
            // A trigger is a part that is not there for anything but touch.
            if (node.Brush is not { } brush || !node.CanCollide || brush.Operation == BrushOperation.Subtractive)
                continue;

            PartHull hull = PartHull.FromBrush(brush, node.WorldMatrix, node.Name);
            if (node.Name == DemoPlayArea.StartDoorName) hull = hull.MovedBy(new Vector3(0f, 0f, doorOpen));

            partIndex[node] = parts.Count;
            parts.Add(hull);
        }

        var sounds = new List<DemoSound>();
        foreach (SceneNode node in scene.Nodes)
        {
            if (node.Entity is not { ClassName: "point_sound" } entity) continue;

            int own = -1;
            for (SceneNode? up = node.Parent; up is not null && own < 0; up = up.Parent)
            {
                if (partIndex.TryGetValue(up, out int index)) own = index;
            }

            Vector3 position = node.WorldPosition;
            if (own >= 0 && parts.Hulls[own].Name == DemoPlayArea.StartDoorName)
                position += new Vector3(0f, 0f, doorOpen);

            sounds.Add(new DemoSound(node.Name, position, Number(entity, "mindistance"), Number(entity, "maxdistance"), own));
        }

        return new DemoWorld(CsgWorld.Build(placements), parts, sounds, placements.Count);
    }

    public DemoSound Sound(string name)
    {
        foreach (DemoSound sound in Sounds)
        {
            if (sound.Name == name) return sound;
        }

        throw new ArgumentException($"The demo has no sound called '{name}'.", nameof(name));
    }

    private static float Number(SpectraEngine.Core.Entities.EntityData entity, string key) =>
        entity.TryGetValue(key, out string text) ? float.Parse(text, CultureInfo.InvariantCulture) : 0f;
}
