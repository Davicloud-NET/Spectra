using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using System;
using SpectraEngine.Core.Serialization;
using System.Text.Json;

namespace SpectraEngine.Core.Maps;

/// <summary>
/// The authored map format's constants: the canonical encoding, every wire
/// member name, and the closed vocabularies. Shared by the reader and the writer.
/// </summary>
public static class MapFormat
{
    /// <summary>Canonical encoding, shared with every other authored document.</summary>
    public static JsonWriterOptions WriterOptions => CanonicalJson.WriterOptions;

    /// <summary>Reader settings, shared with every other authored document.</summary>
    public static JsonReaderOptions ReaderOptions => CanonicalJson.ReaderOptions;

    /// <summary>The bundle's scene document, inside a <c>.smap</c> folder.</summary>
    public const string DocumentFileName = "map.json";

    /// <summary>The extension a map bundle directory carries.</summary>
    public const string BundleExtension = ".smap";

    /// <summary>Per-user editor state. Gitignored; nothing may depend on it.</summary>
    public const string UserStateFileName = "editor.user.json";

    /// <summary>Bundle-relative folder holding script payloads as real files.</summary>
    public const string ScriptsFolderName = "scripts";

    public const string FormatVersionMember = "spectramap";
    public const string MinimumReadableMember = "minimumReadableVersion";
    public const string EngineMember = "engine";
    public const string SceneMember = "scene";
    public const string NodesMember = "nodes";

    public const string IdMember = "id";
    public const string NameMember = "name";
    public const string RealmMember = "realm";
    public const string StateMember = "state";
    public const string KindMember = "kind";
    public const string CollideMember = "collide";
    public const string QueryMember = "query";
    public const string TouchMember = "touch";
    public const string RenderMember = "render";
    public const string TransformMember = "transform";
    public const string BrushMember = "brush";
    public const string MeshMember = "mesh";
    public const string LightMember = "light";
    public const string EntityMember = "entity";
    public const string ChildrenMember = "children";

    public const string ModelMember = "model";
    public const string SubmeshMember = "submesh";

    /// <summary>Reserved at top level and per node. The cook skips it by name without looking inside.</summary>
    public const string EditorMember = "editor";

    public const string PositionMember = "p";
    public const string RotationMember = "r";
    public const string ScaleMember = "s";

    public const string OperationMember = "operation";
    public const string PlanesMember = "planes";
    public const string FacesMember = "faces";
    public const string KeepSourceMember = "keepSource";
    public const string BrushTransformMember = "transform";

    public const string MaterialMember = "material";
    public const string UAxisMember = "u";
    public const string VAxisMember = "v";
    public const string UOffsetMember = "uo";
    public const string VOffsetMember = "vo";
    public const string UScaleMember = "us";
    public const string VScaleMember = "vs";

    public const string ColorMember = "color";
    public const string IntensityMember = "intensity";
    public const string RangeMember = "range";
    public const string EnabledMember = "enabled";

    // Append only, never reorder. Byte identity depends on the canonical member
    // order, so moving one rewrites every file that carries a light.
    public const string InnerAngleMember = "innerAngle";
    public const string OuterAngleMember = "outerAngle";
    public const string WidthMember = "width";
    public const string HeightMember = "height";
    public const string RadiusMember = "radius";

    public const string ClassMember = "class";
    public const string KeysMember = "keys";
    public const string OutputsMember = "outputs";

    public const string OutputMember = "output";
    public const string TargetMember = "target";
    public const string InputMember = "input";
    public const string ParamMember = "param";
    public const string DelayMember = "delay";
    public const string TimesMember = "times";

    /// <summary>
    /// Target names resolved when a wire fires, not against the map. Matched ordinally.
    /// </summary>
    public static readonly string[] RuntimeTargets = ["!self", "!activator", "!caller"];

    /// <summary>A trailing <c>*</c> on a target name matches every name with that prefix.</summary>
    public const char TargetWildcard = '*';

    public const string WorldKind = "world";
    public const string PartKind = "part";

    public const string AdditiveOperation = "additive";
    public const string SubtractiveOperation = "subtractive";

    public const string DirectionalLight = "directional";
    public const string PointLight = "point";
    public const string SpotLight = "spot";
    public const string RectLight = "rect";
    public const string DiscLight = "disc";

    /// <summary>Legal realm names. Core has no enum yet, so they are validated as strings.</summary>
    public static readonly string[] Realms = ["shared", "server", "client"];

    /// <summary>Legal node state names.</summary>
    public static readonly string[] States = ["active", "dormant"];

    internal static string ToWire(BrushKind kind) => kind == BrushKind.Part ? PartKind : WorldKind;

    internal static string ToWire(BrushOperation operation) =>
        operation == BrushOperation.Subtractive ? SubtractiveOperation : AdditiveOperation;

    // Throws on an unknown kind. A default arm would save a new kind under
    // another kind's name.
    internal static string ToWire(LightKind kind) => kind switch
    {
        LightKind.Directional => DirectionalLight,
        LightKind.Point => PointLight,
        LightKind.Spot => SpotLight,
        LightKind.Rect => RectLight,
        LightKind.Disc => DiscLight,
        _ => throw new NotSupportedException($"No wire name for light kind '{kind}'."),
    };

    internal static int IndexOf(string[] order, string member) => Array.IndexOf(order, member);
}
