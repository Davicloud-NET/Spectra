using System;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// A human-scale obstacle course beside the demo room, for walking the
/// character controller. Stairs and ramps bracket the mover's step height
/// and slope limit.
/// </summary>
// Separate from the demo room on purpose: that room is too small for the
// character, and its doorway is the flush-coplanar-cut regression fixture.
// Coordinates are hand-placed against the tuning defaults and should not be
// derived from them. DemoPlayAreaTests walks this level.
public static class DemoPlayArea
{
    /// <summary>Where the course sits, clear of the demo room and the scatter field.</summary>
    public static readonly Vector3 Center = new(150f, 0f, 0f);

    /// <summary>Where the character stands when play mode begins.</summary>
    // Feet just above the slab; the first tick's ground snap settles it.
    public static readonly Vector3 Spawn = new(133f, 0.05f, 0f);

    /// <summary>The starting yaw: looking east (+x), down the course.</summary>
    public const float SpawnYaw = 0f;

    /// <summary>Below this the character has left the course and is respawned.</summary>
    public const float FallOutHeight = -20f;

    /// <summary>The middle of the start room's floor.</summary>
    public static readonly Vector3 StartRoomCenter = new(126f, 0f, 0f);

    /// <summary>The node that carries the start room's player start.</summary>
    public const string PlayerStartName = "PlayerStart";

    /// <summary>The start room's door, a <c>func_door</c>.</summary>
    public const string StartDoorName = "StartDoor";

    /// <summary>The volume in front of the door that opens it, a <c>trigger_multiple</c>.</summary>
    public const string StartZoneName = "StartZone";

    /// <summary>The start room's lift, a <c>func_movelinear</c>.</summary>
    public const string LiftName = "Lift";

    /// <summary>The button that sends the lift up, a <c>func_button</c>.</summary>
    public const string LiftButtonName = "LiftButton";

    // Three units thick so the chasm can be cut straight through it.
    private const float FloorTop = 0f;
    private const float FloorThickness = 3f;

    private const float TerraceTop = 2f;

    // Sized against a 0.35 radius and 1.8 stature.
    private const float OpeningWidth = 1.4f;
    private const float OpeningHeight = 2.2f;

    /// <summary>
    /// Authors the whole course into <paramref name="scene"/> and returns how
    /// many brush nodes it added.
    /// </summary>
    /// <param name="structure">Material for floors, terraces and stairs.</param>
    /// <param name="wall">Material for walls and blocks.</param>
    /// <param name="accent">Material for ramps, cuts and the part brush.</param>
    public static int Build(Scene scene, MaterialRef structure, MaterialRef wall, MaterialRef accent)
    {
        ArgumentNullException.ThrowIfNull(scene);

        int count = 0;

        // 40x40, x in [130,170], z in [-20,20].
        count += Box(scene, "Play.Floor",
            new Vector3(150f, FloorTop - FloorThickness * 0.5f, 0f),
            new Vector3(20f, FloorThickness * 0.5f, 20f), structure);

        // Walls stand on the slab, inner faces at x = 131 / 169, z = -19 / 19.
        count += Box(scene, "Play.WallWest", new Vector3(130.5f, 1.25f, 0f), new Vector3(0.5f, 1.25f, 20f), wall);
        count += Box(scene, "Play.WallEast", new Vector3(169.5f, 1.25f, 0f), new Vector3(0.5f, 1.25f, 20f), wall);
        count += Box(scene, "Play.WallNorth", new Vector3(150f, 1.25f, -19.5f), new Vector3(20f, 1.25f, 0.5f), wall);
        count += Box(scene, "Play.WallSouth", new Vector3(150f, 1.25f, 19.5f), new Vector3(20f, 1.25f, 0.5f), wall);

        // Stairs bracket StepHeight (0.45): all climb 2.0 over 4.0 of run.
        // 0.25 and 0.40 must climb, 0.50 must refuse.
        count += Stairs(scene, "Play.StairsGentle", startX: 135f, treads: 8, rise: 0.25f, tread: 0.5f,
            zCenter: -8f, width: 4f, material: structure);
        count += Stairs(scene, "Play.StairsLimit", startX: 135f, treads: 5, rise: 0.40f, tread: 0.8f,
            zCenter: 0f, width: 4f, material: structure);
        count += Stairs(scene, "Play.StairsTooSteep", startX: 135f, treads: 4, rise: 0.50f, tread: 1.0f,
            zCenter: 8f, width: 4f, material: structure);

        // The wall spans the whole terrace, so the doorway is the only way across.
        count += Box(scene, "Play.Terrace",
            new Vector3(143.5f, TerraceTop - 1f, 0f), new Vector3(4.5f, 1f, 12f), structure);

        count += Box(scene, "Play.DoorWall",
            new Vector3(143.5f, TerraceTop + 1.5f, 0f), new Vector3(0.2f, 1.5f, 12f), wall);

        // The cut's ±x planes coincide with the wall's: the flush coplanar case.
        count += Cut(scene, "Play.DoorCut",
            new Vector3(143.5f, TerraceTop + OpeningHeight * 0.5f, 0f),
            new Vector3(0.2f, OpeningHeight * 0.5f, OpeningWidth * 0.5f), accent);

        // Tunnel through a solid block: a cut with a ceiling to jump into.
        count += Box(scene, "Play.TunnelBlock",
            new Vector3(146f, TerraceTop + 1.5f, 8f), new Vector3(2f, 1.5f, 3f), wall);

        count += Cut(scene, "Play.TunnelCut",
            new Vector3(146f, TerraceTop + OpeningHeight * 0.5f, 8f),
            new Vector3(2f, OpeningHeight * 0.5f, OpeningWidth * 0.5f), accent);

        // Ramps bracket MaxSlopeAngleDegrees (46), same 2.0 rise. 25° and 40°
        // must walk, 55° must slide back down.
        count += Box(scene, "Play.RampPlatform",
            new Vector3(159f, TerraceTop - 1f, -13f), new Vector3(3f, 1f, 7f), structure);

        count += Ramp(scene, "Play.Ramp25", 151.71f, 156f, FloorTop, TerraceTop, -17f, 4f, 0.4f, accent);
        count += Ramp(scene, "Play.Ramp40", 153.62f, 156f, FloorTop, TerraceTop, -13f, 4f, 0.4f, accent);
        count += Ramp(scene, "Play.Ramp55", 154.60f, 156f, FloorTop, TerraceTop, -8.5f, 3f, 0.4f, accent);

        // Chasm: three across, cut through the whole slab. A partial-depth pit
        // cannot be jumped out of at AirSpeedCap = 1.0, so it would be a trap.
        // Top plane flush with the floor's: the one cut here that opens a floor.
        count += Cut(scene, "Play.ChasmCut",
            new Vector3(153.5f, FloorTop - 1.75f, 10f), new Vector3(1.5f, 1.75f, 8f), accent);

        // The only part brush here, so the only collision that does not come
        // from the compiled world. Top at 1.0, reachable with a 1.2 jump.
        count += Part(scene, "Play.PartPlatform",
            new Vector3(164f, 0.8f, -3f), new Vector3(1.5f, 0.2f, 1.5f), accent);

        // Pillars close enough to wedge between: two and three plane contacts.
        count += Box(scene, "Play.PillarA", new Vector3(162f, 1.5f, 8f), new Vector3(0.5f, 1.5f, 0.5f), wall);
        count += Box(scene, "Play.PillarB", new Vector3(164.2f, 1.5f, 8f), new Vector3(0.5f, 1.5f, 0.5f), wall);
        count += Box(scene, "Play.PillarC", new Vector3(163.1f, 1.5f, 10.2f), new Vector3(0.5f, 1.5f, 0.5f), wall);
        count += Box(scene, "Play.PillarD", new Vector3(167f, 1.5f, 14f), new Vector3(0.5f, 1.5f, 0.5f), wall);

        count += StartRoom(scene, structure, wall, accent);

        return count;
    }

    // A room off the west wall where a level begins: a player start, a door
    // in the wall and a volume across the doorway, wired with no code. The
    // classes are named as text, so a host that registers none of them gets a
    // door that stays shut.
    // Entity nodes have plain names, since people type them into wires and
    // into the console.
    private static int StartRoom(Scene scene, MaterialRef structure, MaterialRef wall, MaterialRef accent)
    {
        int count = 0;

        // Inside: x in [122,130], z in [-4,4]. The east side is the course's
        // west wall. The floor reaches under the room's own walls.
        count += Box(scene, "Play.StartFloor",
            new Vector3(125.5f, -0.5f, 0f), new Vector3(4.5f, 0.5f, 5f), structure);
        count += Box(scene, "Play.StartWallWest",
            new Vector3(121.5f, 1.25f, 0f), new Vector3(0.5f, 1.25f, 5f), wall);
        count += Box(scene, "Play.StartWallNorth",
            new Vector3(126f, 1.25f, -4.5f), new Vector3(4f, 1.25f, 0.5f), wall);
        count += Box(scene, "Play.StartWallSouth",
            new Vector3(126f, 1.25f, 4.5f), new Vector3(4f, 1.25f, 0.5f), wall);

        // Through the west wall, flush with both of its faces.
        var doorway = new Vector3(130.5f, OpeningHeight * 0.5f, 0f);
        count += Cut(scene, "Play.StartDoorCut",
            doorway, new Vector3(0.5f, OpeningHeight * 0.5f, OpeningWidth * 0.5f), accent);

        // Thinner than the wall, so it slides along z out of sight inside it.
        SceneNode door = PartNode(scene, StartDoorName,
            doorway, new Vector3(0.4f, OpeningHeight * 0.5f, OpeningWidth * 0.5f), accent);
        count++;
        door.Entity = new Entities.EntityData("func_door");
        door.Entity.SetValue("movedir", "0 0 1");
        door.Entity.SetValue("speed", "3");
        door.Entity.SetValue("wait", "3");

        // Across the doorway and out to both sides, so the door opens from
        // either. It stops short of Spawn: a level with no start begins there.
        SceneNode zone = PartNode(scene, StartZoneName,
            new Vector3(130.15f, OpeningHeight * 0.5f, 0f),
            new Vector3(2.15f, OpeningHeight * 0.5f, 1.5f), accent);
        count++;
        zone.CanCollide = false;
        zone.CanQuery = false;
        zone.IsRendered = false;
        zone.Entity = new Entities.EntityData("trigger_multiple");
        zone.Entity.Connections.Add(new Entities.EntityConnection(
            "OnTrigger", StartDoorName, "Open", "", 0f, Entities.EntityConnection.Infinite));

        // Its +z is the way the player faces: east, at the door.
        SceneNode start = scene.Root.CreateChild(PlayerStartName);
        start.LocalPosition = new Vector3(125f, 0f, 0f);
        start.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        start.Entity = new Entities.EntityData("info_player_start");

        // A lift in the north-west corner. It lies on the floor, low enough
        // to step onto, and rises until its top is level with the walls. Up
        // there it waits three seconds and comes down by a wire to itself.
        SceneNode lift = PartNode(scene, LiftName,
            new Vector3(123f, 0.15f, -3f), new Vector3(1f, 0.15f, 1f), structure);
        count++;
        lift.Entity = new Entities.EntityData("func_movelinear");
        lift.Entity.SetValue("distance", "2.2");
        lift.Entity.SetValue("speed", "1.5");
        lift.Entity.Connections.Add(new Entities.EntityConnection(
            "OnFullyOpen", LiftName, "Close", "", 3f, Entities.EntityConnection.Infinite));

        // On the west wall beside the lift, not over it, so the lift does not
        // pass through it. In reach of someone standing on the lift, and tall
        // enough to be in front of the eye from there and from the floor.
        SceneNode button = PartNode(scene, LiftButtonName,
            new Vector3(122.075f, 1.75f, -1.5f), new Vector3(0.075f, 0.3f, 0.3f), accent);
        count++;
        button.Entity = new Entities.EntityData("func_button");
        button.Entity.SetValue("movedir", "-1 0 0");
        button.Entity.Connections.Add(new Entities.EntityConnection(
            "OnPressed", LiftName, "Open", "", 0f, Entities.EntityConnection.Infinite));

        return count;
    }

    // Placement on the node, size in the brush. Extents must be symmetric:
    // CreateBox puts an off-centre box's offset in the brush's own Transform,
    // which a node placement ignores.

    private static int Box(Scene scene, string name, Vector3 center, Vector3 half, MaterialRef material)
    {
        var node = scene.Root.CreateChild(name);
        node.LocalPosition = center;
        node.Brush = Brush.CreateBox(-half, half, material);
        return 1;
    }

    private static int Cut(Scene scene, string name, Vector3 center, Vector3 half, MaterialRef material)
    {
        var node = scene.Root.CreateChild(name);
        node.LocalPosition = center;
        node.Brush = Brush.CreateBox(-half, half, material).WithOperation(BrushOperation.Subtractive);
        return 1;
    }

    private static int Part(Scene scene, string name, Vector3 center, Vector3 half, MaterialRef material)
    {
        PartNode(scene, name, center, half, material);
        return 1;
    }

    private static SceneNode PartNode(
        Scene scene, string name, Vector3 center, Vector3 half, MaterialRef material)
    {
        var node = scene.Root.CreateChild(name);
        node.LocalPosition = center;
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(-half, half, material);
        return node;
    }

    // One box per tread, each reaching down into the floor. Thin slabs would
    // leave the risers open underneath, which the step probe would find.
    private static int Stairs(
        Scene scene, string name, float startX, int treads, float rise, float tread,
        float zCenter, float width, MaterialRef material)
    {
        for (int i = 0; i < treads; i++)
        {
            float top = FloorTop + rise * (i + 1);
            float centerX = startX + tread * i + tread * 0.5f;

            Box(scene, $"{name}{i}",
                new Vector3(centerX, (top + FloorTop - FloorThickness) * 0.5f, zCenter),
                new Vector3(tread * 0.5f, (top - FloorTop + FloorThickness) * 0.5f, width * 0.5f),
                material);
        }

        return treads;
    }

    // A rotated box. The endpoints are those of the top surface; the box hangs
    // below it and sinks into the floor.
    private static int Ramp(
        Scene scene, string name, float startX, float endX, float startY, float endY,
        float zCenter, float width, float thickness, MaterialRef material)
    {
        float dx = endX - startX;
        float dy = endY - startY;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        float angle = MathF.Atan2(dy, dx);

        // Local +y after the rotation about +z: the surface normal.
        var up = new Vector3(-MathF.Sin(angle), MathF.Cos(angle), 0f);
        var topCenter = new Vector3((startX + endX) * 0.5f, (startY + endY) * 0.5f, zCenter);

        // Deep enough to reach the floor at every angle, so no gap under the toe.
        float depth = thickness + (endY - startY) + 1f;

        var node = scene.Root.CreateChild(name);
        node.LocalPosition = topCenter - up * (depth * 0.5f);
        node.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);

        var half = new Vector3(length * 0.5f, depth * 0.5f, width * 0.5f);
        node.Brush = Brush.CreateBox(-half, half, material);
        return 1;
    }
}
