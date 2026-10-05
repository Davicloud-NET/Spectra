using System;
using System.Numerics;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

// The shared compiled-map fixture. Every value is a literal, so two builds
// give the same bytes. Assets, node names and cells are all added out of
// sorted order, so the ordering rules have something to do.
internal static class ScmapFixture
{
    // SCPT, LUAB, LUAS.
    public const int ReservedEmptySections = 3;

    public const string SceneName = "Determinism";

    // In reference order.
    public static readonly string[] AssetPaths =
    [
        "Materials/zulu.spectramat",
        "Materials/alpha.spectramat",
        "Textures/mid.png",
        "Models/crate.smodel",
    ];

    // In pre-order.
    public static readonly string[] NodeNames =
    [
        "World",
        "zeta_room",
        "Wall",
        "Cut",
        "alpha_room",
        "Lamp",
        "Crate",
    ];

    // What the entities add to the string table, in first-reference order: per
    // entity its class, its keys and values, then each wire's output, target,
    // input and parameter. A string already in the table is not added again.
    public static readonly string[] EntityStrings =
    [
        "logic_auto", "OnMapSpawn", "Trigger",
        "logic_relay", "delay", "0.5", "tag", "first", "second", "OnTrigger", "Open", "zeta_*", "Kill", "now",
        "func_door", "speed", "100",
    ];

    // Empty string, scene name, asset paths, node names, entity strings.
    public static string[] ExpectedStrings() =>
        [string.Empty, SceneName, .. AssetPaths, .. NodeNames, .. EntityStrings];

    // In node order. The first has only wires, the last only a keyvalue, and
    // the middle one repeats a key.
    public static readonly ScmapEntitySource[] Entities =
    [
        new(1, "logic_auto",
            [],
            [new EntityConnection("OnMapSpawn", "Lamp", "Trigger", "", 0f, 1)]),

        new(5, "logic_relay",
            [new("delay", "0.5"), new("tag", "first"), new("tag", "second")],
            [
                new EntityConnection("OnTrigger", "Crate", "Open", "", 0f, EntityConnection.Infinite),
                new EntityConnection("OnTrigger", "zeta_*", "Kill", "now", 1.5f, 3),
            ]),

        new(6, "func_door",
            [new("speed", "100")],
            []),
    ];

    // One hull per baked brush, in node order: a box on "Wall" and a
    // five-plane wedge on "Cut", so the two plane counts differ.
    public static readonly ScmapCollisionHullSource[] Hulls =
    [
        new(2,
        [
            new Plane(1f, 0f, 0f, -0.5f), new Plane(-1f, 0f, 0f, -0.5f),
            new Plane(0f, 1f, 0f, -0.5f), new Plane(0f, -1f, 0f, -0.5f),
            new Plane(0f, 0f, 1f, -0.5f), new Plane(0f, 0f, -1f, -0.5f),
        ]),

        new(3,
        [
            new Plane(0f, -1f, 0f, -0.5f),
            new Plane(0f, 0f, 1f, -0.5f), new Plane(0f, 0f, -1f, -0.5f),
            new Plane(0.70710677f, 0.70710677f, 0f, -0.35355338f),
            new Plane(-0.70710677f, 0.70710677f, 0f, -0.35355338f),
        ]),
    ];

    // What each hull plane's face is made of, as asset rows, for a fixture
    // built with hull materials. Rows 0 and 1 are the two materials. The
    // sentinel is a face that names none.
    public static readonly uint[][] HullFaceAssets =
    [
        [0, 1, 0, ScmapFormat.NoAssetIndex, 1, 1],
        [1, ScmapFormat.NoAssetIndex, 0, 0, 1],
    ];

    // In node order. The first is a light left at its defaults, the second
    // sets every field to something else.
    public static readonly ScmapLightSource[] Lights =
    [
        new(0, new Light()),

        new(5, new Light
        {
            Kind = LightKind.Spot,
            Color = new Vector3(1f, 0.5f, 0.25f),
            Intensity = 3.5f,
            Range = 12.25f,
            Enabled = false,
            InnerAngle = 15f,
            OuterAngle = 40f,
            Width = 2f,
            Height = 0.75f,
            Radius = 0.3f,
        }),
    ];

    // What a trigger volume wears: no collision, no queries, not drawn, touch on.
    public const ScmapPayloadFlags LampFlags =
        ScmapPayloadFlags.NoCollide | ScmapPayloadFlags.NoQuery | ScmapPayloadFlags.NoRender;

    // Index-aligned to NodeNames. Public so the bit-identity test compares the
    // file against the authored values, not against itself.
    public static readonly Transform[] Transforms =
    [
        Identity,
        Placed(12.5f, 0f, -4.25f),
        Placed(1.0000001f, -3.3333333f, 1.4012985e-45f),
        Placed(-0.5f, 2.5f, 0.5f),
        Placed(-64f, 0f, 128f),
        Placed(0f, 3f, 0f),
        Placed(2f, 0.5f, -2f),
    ];

    public static byte[] Build(bool withHullMaterials = false) =>
        CreateBuilder(withHullMaterials).Build(Digest, EngineInfo.MapFormatVersion);

    public static UInt128 Digest => new(0x0123456789ABCDEFul, 0xFEDCBA9876543210ul);

    public static ScmapBuilder CreateBuilder(bool withHullMaterials = false)
    {
        var builder = new ScmapBuilder(SceneName);

        builder.AddSpawn(new ScmapSpawnSource(
            new Vector3(0f, 64.5f, -12.25f),
            Quaternion.CreateFromYawPitchRoll(0.75f, 0f, 0f)));

        builder.AddAsset(new ScmapAssetSource(PackEntryKind.Material, AssetPaths[0], 0x1111_2222_3333_4444ul));
        builder.AddAsset(new ScmapAssetSource(PackEntryKind.Material, AssetPaths[1], 0x5555_6666_7777_8888ul));
        builder.AddAsset(new ScmapAssetSource(PackEntryKind.Image, AssetPaths[2], 0x9999_AAAA_BBBB_CCCCul));
        builder.AddAsset(new ScmapAssetSource(PackEntryKind.Model, AssetPaths[3], 0xDDDD_EEEE_FFFF_0000ul));

        // Pre-order: a parent's index is below its child's.
        builder.AddNode(new ScmapNodeSource(
            NodeId(0), NodeNames[0], -1, Transforms[0], ScmapPayloadKind.None));

        builder.AddNode(new ScmapNodeSource(
            NodeId(1), NodeNames[1], 0, Transforms[1], ScmapPayloadKind.None));

        builder.AddNode(new ScmapNodeSource(
            NodeId(2), NodeNames[2], 1, Transforms[2],
            ScmapPayloadKind.StaticWorldBrush,
            ScmapPayloadFlags.NoTouch));

        builder.AddNode(new ScmapNodeSource(
            NodeId(3), NodeNames[3], 1, Transforms[3],
            ScmapPayloadKind.StaticWorldBrush,
            ScmapPayloadFlags.SubtractiveBrush));

        builder.AddNode(new ScmapNodeSource(
            NodeId(4), NodeNames[4], 0, Transforms[4], ScmapPayloadKind.None));

        builder.AddNode(new ScmapNodeSource(
            NodeId(5), NodeNames[5], 4, Transforms[5], ScmapPayloadKind.None, LampFlags));

        builder.AddNode(new ScmapNodeSource(
            NodeId(6), NodeNames[6], 4, Transforms[6],
            ScmapPayloadKind.MeshInstance,
            ScmapPayloadFlags.IsEntityOwned,
            PayloadIndex: 3));

        foreach (ScmapEntitySource entity in Entities) builder.AddEntity(entity);
        for (int i = 0; i < Hulls.Length; i++)
        {
            builder.AddCollisionHull(
                withHullMaterials ? Hulls[i] with { FaceAssets = HullFaceAssets[i] } : Hulls[i]);
        }

        foreach (ScmapLightSource light in Lights) builder.AddLight(light);

        // Unsorted on every axis.
        builder.AddChunk(Cell(2, 0, -1));
        builder.AddChunk(Cell(-3, 4, 0));
        builder.AddChunk(Cell(2, 0, -9));
        builder.AddChunk(Cell(-3, 1, 7));
        builder.AddChunk(Cell(0, 0, 0));

        return builder;
    }

    // The fixture's cells in directory order.
    public static ChunkCoord[] SortedCells =>
    [
        new(-3, 1, 7),
        new(-3, 4, 0),
        new(0, 0, 0),
        new(2, 0, -9),
        new(2, 0, -1),
    ];

    public static Guid NodeId(int index) =>
        Guid.Parse($"3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3{index:x2}");

    private static Transform Identity => new()
    {
        Position = Vector3.Zero,
        Rotation = Quaternion.Identity,
        Scale = Vector3.One,
    };

    private static Transform Placed(float x, float y, float z) => new()
    {
        // Non-trivial rotation and scale, so all ten floats are exercised.
        Position = new Vector3(x, y, z),
        Rotation = Quaternion.Normalize(new Quaternion(0.1f, -0.7f, 0.3f, 0.64f)),
        Scale = new Vector3(1.5f, 0.25f, 3.0000002f),
    };

    private static ScmapChunkSource Cell(int x, int y, int z)
    {
        var coord = new ChunkCoord(x, y, z);

        // Not the cell cube: render bounds can overhang the cell.
        return new ScmapChunkSource(
            coord,
            new Aabb(coord.MinCorner - new Vector3(0.5f), coord.MaxCorner + new Vector3(1.25f)));
    }
}
