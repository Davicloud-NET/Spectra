using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Maps;

/// <summary>
/// An authored map as it sits on disk. <see cref="MapReader"/> and
/// <see cref="MapWriter"/> round-trip it byte for byte;
/// <see cref="MapSceneBinder"/> projects it to and from a live <see cref="Scene.Scene"/>.
/// </summary>
// Separate from the scene because a scene is a lossy image of a document: Brush
// re-normalises its planes, and unknown members have nowhere to live on a
// SceneNode. Members the engine cannot bind yet are carried, not dropped.
public sealed class MapDocument
{
    // Canonical top-level order. Indices anchor preserved members.
    internal static readonly string[] MemberOrder =
        [MapFormat.FormatVersionMember, MapFormat.MinimumReadableMember, MapFormat.EngineMember,
         MapFormat.SceneMember, MapFormat.EditorMember, MapFormat.NodesMember];

    /// <summary>The document's own format version.</summary>
    public int FormatVersion { get; set; } = EngineInfo.MapFormatVersion;

    /// <summary>
    /// The oldest reader version that can read this document. A reader refuses a
    /// document whose value here exceeds what it implements.
    /// </summary>
    public int MinimumReadableVersion { get; set; } = EngineInfo.MinimumReadableMapVersion;

    /// <summary>Engine version that last wrote this document. Informational; never a load gate.</summary>
    public string Engine { get; set; } = EngineInfo.VersionString;

    /// <summary>Scene-level settings: the name, plus anything preserved.</summary>
    public MapSceneInfo Scene { get; set; } = new();

    /// <summary>The reserved <c>editor</c> key, carried verbatim. The cook drops it by name.</summary>
    // Raw bytes because its content belongs to SpectraEngine.Editing, which Core
    // cannot reference.
    public PreservedValue? Editor { get; set; }

    /// <summary>The root's children, in order. Depth is expressed by nesting.</summary>
    // Sibling order is static-world placement order, which breaks ties in the
    // carve, so it must survive. No record for the root: Scene.Root mints its own id.
    public List<MapNode> Nodes { get; } = [];

    /// <summary>Top-level members this engine version does not recognise.</summary>
    public List<PreservedMember> Unknown { get; } = [];
}

/// <summary>Scene-level settings.</summary>
// scene.spawn is in the format but Scene has no spawn to bind it to, so it
// round-trips as a preserved member.
public sealed class MapSceneInfo
{
    internal static readonly string[] MemberOrder = [MapFormat.NameMember];

    public string Name { get; set; } = "Scene";

    public List<PreservedMember> Unknown { get; } = [];
}

/// <summary>One authored node.</summary>
public sealed class MapNode
{
    // Indices anchor preserved members and are recomputed on every read, so
    // inserting a name here renumbers nothing in a file.
    internal static readonly string[] MemberOrder =
        [MapFormat.IdMember, MapFormat.NameMember, MapFormat.RealmMember, MapFormat.StateMember,
         MapFormat.KindMember, MapFormat.TransformMember, MapFormat.BrushMember, MapFormat.MeshMember,
         MapFormat.LightMember, MapFormat.EntityMember, MapFormat.EditorMember, MapFormat.ChildrenMember];

    public Guid Id { get; set; }

    public string Name { get; set; } = "Node";

    /// <summary>
    /// Byte offset this node started at in the source document, or 0 when it
    /// was built in code. Not persisted.
    /// </summary>
    // Lets an error raised while building the scene (a bad plane set that is
    // still valid JSON) point into the file.
    public long SourceOffset { get; set; }

    /// <summary>The node's own declared realm, or null when omitted (meaning inherit).</summary>
    // Validated on read though nothing binds it yet. A typo kept as an unknown
    // member would fall through to shared.
    public string? Realm { get; set; }

    /// <summary>The node's own declared state, or null when omitted.</summary>
    public string? State { get; set; }

    /// <summary>Declared brush kind, or null when omitted (meaning <c>World</c>).</summary>
    public BrushKind? Kind { get; set; }

    public MapTransform Transform { get; set; } = MapTransform.Identity;

    public MapBrush? Brush { get; set; }

    /// <summary>The model file this node's mesh came from. Null for a mesh built in code.</summary>
    public MapMeshSource? Mesh { get; set; }

    /// <summary>The node's light, if any.</summary>
    public MapLight? Light { get; set; }

    /// <summary>The entity this node is, if any: its class, keyvalues and outgoing wires.</summary>
    // Must be bound, not preserved: the binder builds a fresh MapNode from the
    // scene on save, so a preserved entity would be deleted.
    public MapEntity? Entity { get; set; }

    /// <summary>The reserved per-node <c>editor</c> key, carried verbatim.</summary>
    public PreservedValue? Editor { get; set; }

    public List<MapNode> Children { get; } = [];

    /// <summary>Members this engine version does not recognise, such as <c>script</c>.</summary>
    public List<PreservedMember> Unknown { get; } = [];
}

/// <summary>The authored 10-float transform, as stored. Never a world matrix.</summary>
// Use Identity, not default: default has zero scale and a zero quaternion.
public struct MapTransform
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public static MapTransform Identity =>
        new() { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One };
}

/// <summary>
/// A brush: planes, per-plane surfaces, and the sign. A closed record: the
/// reader refuses an unknown member, since any member here can change the solid.
/// </summary>
public sealed class MapBrush
{
    public BrushOperation Operation { get; set; } = BrushOperation.Additive;

    // A node-attached brush ignores this, but the standalone Csg.Carve and
    // CsgWorld.Build overloads read it, so it is kept.
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

    /// <summary>Planes as <c>[nx, ny, nz, d]</c>, matching <c>System.Numerics.Plane</c>'s field order. Normals point out of the solid.</summary>
    public List<Vector4> Planes { get; } = [];

    /// <summary>Per-plane surfaces, indexed by plane index. Length must equal <see cref="Planes"/>.</summary>
    public List<MapFace> Faces { get; } = [];

    /// <summary>Whether the cook keeps this brush's authored planes in the compiled map.</summary>
    public bool KeepSource { get; set; }
}

/// <summary>
/// One brush face's material and texture projection. An open record: unknown
/// members are preserved, since none can change the solid.
/// </summary>
public sealed class MapFace
{
    internal static readonly string[] MemberOrder =
        [MapFormat.MaterialMember, MapFormat.UAxisMember, MapFormat.VAxisMember,
         MapFormat.UOffsetMember, MapFormat.VOffsetMember, MapFormat.UScaleMember, MapFormat.VScaleMember];

    /// <summary>
    /// Content-root-relative path of a <c>.spectramat</c>, or null for the
    /// engine default material.
    /// </summary>
    // A path, never MaterialRef.Id: ids depend on interning order in this process.
    public string? Material { get; set; }

    /// <summary>Explicit U axis, or null for world-aligned.</summary>
    public Vector3? UAxis { get; set; }

    /// <summary>Explicit V axis, or null for world-aligned.</summary>
    public Vector3? VAxis { get; set; }

    public float UOffset { get; set; }
    public float VOffset { get; set; }

    /// <summary>World units per texture repeat. Defaults to 1; zero is refused by <c>FaceSurface</c>.</summary>
    public float UScale { get; set; } = 1f;
    public float VScale { get; set; } = 1f;

    public List<PreservedMember> Unknown { get; } = [];
}

/// <summary>A reference to one submesh of a model file. An open record.</summary>
public sealed class MapMeshSource
{
    internal static readonly string[] MemberOrder = [MapFormat.ModelMember, MapFormat.SubmeshMember];

    /// <summary>Content-root-relative path of the model file.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Index into the model's submesh list. Omitted when zero.</summary>
    public int Submesh { get; set; }

    public List<PreservedMember> Unknown { get; } = [];
}

/// <summary>A light payload.</summary>
public sealed class MapLight
{
    // Append only. Byte identity depends on this order, so a new member goes
    // at the end.
    internal static readonly string[] MemberOrder =
        [MapFormat.KindMember, MapFormat.ColorMember, MapFormat.IntensityMember,
         MapFormat.RangeMember, MapFormat.EnabledMember,
         MapFormat.InnerAngleMember, MapFormat.OuterAngleMember,
         MapFormat.WidthMember, MapFormat.HeightMember, MapFormat.RadiusMember];

    public LightKind Kind { get; set; } = LightKind.Directional;

    /// <summary>Linear RGB, not a display colour.</summary>
    public Vector3 Color { get; set; } = Vector3.One;

    public float Intensity { get; set; } = 1f;

    /// <summary>Falloff range. Written whenever it differs from 10.</summary>
    // Never default this to zero: Light.Range throws on anything not positive.
    public float Range { get; set; } = 10f;

    public bool Enabled { get; set; } = true;

    /// <summary>A spot's fully-lit half-angle in degrees.</summary>
    public float InnerAngle { get; set; } = 25f;

    /// <summary>A spot's outer half-angle in degrees.</summary>
    public float OuterAngle { get; set; } = 35f;

    /// <summary>A rect light's width in world units.</summary>
    public float Width { get; set; } = 1f;

    /// <summary>A rect light's height in world units.</summary>
    public float Height { get; set; } = 1f;

    /// <summary>A disc light's radius in world units.</summary>
    public float Radius { get; set; } = 0.5f;

    public List<PreservedMember> Unknown { get; } = [];
}

/// <summary>
/// An entity payload: a class name, its keyvalues, and its outgoing wires. An
/// open record. The class is text and is not resolved here, so a map naming a
/// class this build lacks still loads and saves unchanged.
/// </summary>
public sealed class MapEntity
{
    // Append only, as on MapLight.
    internal static readonly string[] MemberOrder =
        [MapFormat.ClassMember, MapFormat.KeysMember, MapFormat.OutputsMember];

    /// <summary>The entity class this node is, as the file spells it.</summary>
    public string Class { get; set; } = string.Empty;

    /// <summary>The authored keyvalues in authored order, values as text.</summary>
    // A list, not a dictionary: order must round-trip and a hand-written
    // duplicate key must survive.
    public List<KeyValuePair<string, string>> Keys { get; } = [];

    /// <summary>The wires leaving this entity's outputs, in authored order.</summary>
    public List<MapConnection> Outputs { get; } = [];

    public List<PreservedMember> Unknown { get; } = [];
}

/// <summary>
/// One authored wire, the document's image of
/// <see cref="Entities.EntityConnection"/>. An open record.
/// </summary>
// Unknown members here do not survive a trip through a scene: EntityConnection
// has nowhere to keep raw bytes.
public sealed class MapConnection
{
    internal static readonly string[] MemberOrder =
        [MapFormat.OutputMember, MapFormat.TargetMember, MapFormat.InputMember,
         MapFormat.ParamMember, MapFormat.DelayMember, MapFormat.TimesMember];

    /// <summary>The output that fires this wire.</summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>The name of the entity or entities to send to. Matches <c>SceneNode.Name</c>.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>The input to send.</summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>The argument to send. Omitted when empty.</summary>
    public string Param { get; set; } = string.Empty;

    /// <summary>Seconds to wait before sending. Omitted at zero.</summary>
    public float Delay { get; set; }

    /// <summary>
    /// How many times this wire may fire, or
    /// <see cref="Entities.EntityConnection.Infinite"/>. Omitted at infinite.
    /// </summary>
    public int Times { get; set; } = Entities.EntityConnection.Infinite;

    public List<PreservedMember> Unknown { get; } = [];
}
