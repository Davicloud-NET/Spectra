namespace Spectra.Kitchen.Diagnostics;

/// <summary>
/// Every <c>SC####</c> code the cooker issues. The thousands digit is the
/// subsystem; see <see cref="DescribeBand"/>.
/// </summary>
// Severity is not chosen here. CookGate classifies every code.
// Some codes are issued by both a rule and the verifier: the band names the
// subsystem that failed, wherever it was found.
public static class CookDiagnosticCodes
{
    /// <summary>The path named is not a Spectra project.</summary>
    public static readonly CookDiagnosticId ProjectNotOpened = CookDiagnosticId.Cook(1);

    /// <summary>A verb the tool accepts and has not built yet.</summary>
    public static readonly CookDiagnosticId VerbNotImplemented = CookDiagnosticId.Cook(2);

    /// <summary>An option the tool accepts and does not act on yet.</summary>
    public static readonly CookDiagnosticId OptionNotImplemented = CookDiagnosticId.Cook(3);

    /// <summary>The output location could not be written.</summary>
    public static readonly CookDiagnosticId OutputNotWritable = CookDiagnosticId.Cook(4);

    /// <summary>A clean was asked to delete something that is not cook output.</summary>
    public static readonly CookDiagnosticId UnsafeCleanTarget = CookDiagnosticId.Cook(5);

    /// <summary>The project has no content root to walk.</summary>
    public static readonly CookDiagnosticId ContentRootMissing = CookDiagnosticId.Cook(1001);

    /// <summary>A rule read or probed a path that is not in the content root.</summary>
    public static readonly CookDiagnosticId InputMissing = CookDiagnosticId.Cook(1002);

    /// <summary>A path a rule named cannot be a content path at all.</summary>
    public static readonly CookDiagnosticId InputPathInvalid = CookDiagnosticId.Cook(1003);

    /// <summary>A rule failed for a reason it did not report itself.</summary>
    public static readonly CookDiagnosticId RuleFailed = CookDiagnosticId.Cook(1004);

    /// <summary>Content the cook found and has no rule for, so it is not in the artifact.</summary>
    public static readonly CookDiagnosticId ContentNotCooked = CookDiagnosticId.Cook(1005);

    /// <summary>The cook cache could not be written. The artifact is still correct.</summary>
    public static readonly CookDiagnosticId CacheNotWritable = CookDiagnosticId.Cook(1006);

    /// <summary>A cook cache on disk could not be read and was discarded.</summary>
    public static readonly CookDiagnosticId CacheDiscarded = CookDiagnosticId.Cook(1007);

    /// <summary>An image file the decoder could not read.</summary>
    public static readonly CookDiagnosticId ImageUndecodable = CookDiagnosticId.Cook(2001);

    /// <summary>
    /// An image decoded and could not be turned into a cooked container. A cooker
    /// fault, not the author's file.
    /// </summary>
    public static readonly CookDiagnosticId ImageEncodeFailed = CookDiagnosticId.Cook(2002);

    /// <summary>A cooked image in a pack is not a readable <c>.simage</c>. From the verifier.</summary>
    public static readonly CookDiagnosticId ImageFileUnreadable = CookDiagnosticId.Cook(2003);

    /// <summary>A model file the glTF reader could not read. Carries the reader's message.</summary>
    public static readonly CookDiagnosticId ModelUndecodable = CookDiagnosticId.Cook(3001);

    /// <summary>
    /// A model names a material this project does not author, so the cooked
    /// submesh gets the default material. Authoring
    /// <c>Materials/&lt;name&gt;.spectramat</c> fixes it.
    /// </summary>
    // Soft: the glTF is valid, the limit is .smodel v1's.
    public static readonly CookDiagnosticId ModelMaterialUnauthored = CookDiagnosticId.Cook(3002);

    /// <summary>
    /// A model read and could not be written into a cooked container. A cooker
    /// fault, not the author's file.
    /// </summary>
    public static readonly CookDiagnosticId ModelEncodeFailed = CookDiagnosticId.Cook(3003);

    /// <summary>
    /// A model carried something the cooked format drops: a second UV set,
    /// tangents, vertex colours, skins, morph targets.
    /// </summary>
    public static readonly CookDiagnosticId ModelDataDropped = CookDiagnosticId.Cook(3004);

    /// <summary>A cooked model in a pack is not a readable <c>.smodel</c>. From the verifier.</summary>
    public static readonly CookDiagnosticId ModelFileUnreadable = CookDiagnosticId.Cook(3005);

    /// <summary>A cooked model names a material that is not in the pack. From the verifier.</summary>
    public static readonly CookDiagnosticId ModelMaterialMissing = CookDiagnosticId.Cook(3006);

    /// <summary>An audio file the decoder could not read.</summary>
    public static readonly CookDiagnosticId AudioUndecodable = CookDiagnosticId.Cook(4001);

    /// <summary>
    /// An audio file decoded and could not be written into a cooked container. A
    /// cooker fault, not the author's file.
    /// </summary>
    public static readonly CookDiagnosticId AudioEncodeFailed = CookDiagnosticId.Cook(4002);

    /// <summary>
    /// A stereo sound whose name does not end <c>_2d</c>. OpenAL does not
    /// spatialise stereo, so it would play flat with no error.
    /// </summary>
    public static readonly CookDiagnosticId AudioStereoPositional = CookDiagnosticId.Cook(4003);

    /// <summary>A sound was resampled to the project rate.</summary>
    public static readonly CookDiagnosticId AudioResampled = CookDiagnosticId.Cook(4004);

    /// <summary>A loop the source declared that the cooked format cannot carry, so it was dropped.</summary>
    public static readonly CookDiagnosticId AudioLoopUnusable = CookDiagnosticId.Cook(4005);

    /// <summary>A cooked sound in a pack is not a readable <c>.saudio</c>. From the verifier.</summary>
    public static readonly CookDiagnosticId AudioFileUnreadable = CookDiagnosticId.Cook(4006);

    /// <summary>A material names a texture that is not there.</summary>
    public static readonly CookDiagnosticId MaterialTextureMissing = CookDiagnosticId.Cook(5001);

    /// <summary>A material has a line the parser could not use.</summary>
    // Soft: the parser tolerates unknown keys so files stay forward-compatible.
    public static readonly CookDiagnosticId MaterialFileMalformed = CookDiagnosticId.Cook(5002);

    /// <summary>
    /// A material names a shader that nothing in the content provides. A name
    /// only a host's shader resolver knows is reported too.
    /// </summary>
    public static readonly CookDiagnosticId MaterialShaderMissing = CookDiagnosticId.Cook(5003);

    // A compiler diagnostic should travel under its own SS#### code via Wrap.
    // The compiler has no numbers yet, so SC6001 stands in. Retire it, don't
    // reuse it, once the compiler numbers its diagnostics.

    /// <summary>The shader compiler refused a source file. Carries its message verbatim.</summary>
    public static readonly CookDiagnosticId ShaderCompileFailed = CookDiagnosticId.Cook(6001);

    /// <summary>
    /// A cooked shader has no blob for a backend it was cooked for. The engine
    /// would fall back to compiling from source at every launch.
    /// </summary>
    public static readonly CookDiagnosticId ShaderBackendMissing = CookDiagnosticId.Cook(6002);

    /// <summary>A cooked shader's payload is not a readable <c>.specshadecomp</c> file.</summary>
    public static readonly CookDiagnosticId ShaderFileUnreadable = CookDiagnosticId.Cook(6003);

    /// <summary>A target backend this toolchain has no code generator for.</summary>
    public static readonly CookDiagnosticId ShaderBackendUnsupported = CookDiagnosticId.Cook(6004);

    /// <summary>A shader was cooked with an empty target list, so it produced nothing.</summary>
    public static readonly CookDiagnosticId ShaderNoTargets = CookDiagnosticId.Cook(6005);

    /// <summary>
    /// A brush node's world transform is not rigid. The message should name the
    /// node, since the scale is often on an ancestor.
    /// </summary>
    public static readonly CookDiagnosticId MapBrushNonRigid = CookDiagnosticId.Cook(7001);

    /// <summary>A plane set <c>Brush</c>'s own constructor rejects.</summary>
    public static readonly CookDiagnosticId MapBrushRefused = CookDiagnosticId.Cook(7002);

    /// <summary>Two scene nodes in one map claim the same Guid.</summary>
    public static readonly CookDiagnosticId MapNodeIdDuplicate = CookDiagnosticId.Cook(7003);

    /// <summary>A brush record's face count does not match its plane count.</summary>
    public static readonly CookDiagnosticId MapFaceCountMismatch = CookDiagnosticId.Cook(7004);

    /// <summary>An entity connection names a target no node carries. The connection is kept.</summary>
    public static readonly CookDiagnosticId MapConnectionTargetMissing = CookDiagnosticId.Cook(7005);

    /// <summary>An entity names a classname this build has no schema for.</summary>
    public static readonly CookDiagnosticId MapEntityClassUnknown = CookDiagnosticId.Cook(7006);

    /// <summary>A map bundle's document is not a readable <c>.smap</c>.</summary>
    public static readonly CookDiagnosticId MapDocumentMalformed = CookDiagnosticId.Cook(7007);

    /// <summary>
    /// A compiled map names an asset that is not in the pack it ships in. From
    /// the verifier.
    /// </summary>
    public static readonly CookDiagnosticId MapAssetMissing = CookDiagnosticId.Cook(7008);

    /// <summary>
    /// A compiled map this engine's own reader refuses, for example one baked at
    /// another format version. From the verifier.
    /// </summary>
    public static readonly CookDiagnosticId MapFileUnreadable = CookDiagnosticId.Cook(7009);

    /// <summary>
    /// A node carries an entity and a world brush. The brush is baked into the
    /// level, so the entity cannot move or hide it.
    /// </summary>
    public static readonly CookDiagnosticId MapEntityOnWorldBrush = CookDiagnosticId.Cook(7010);

    /// <summary>A script the Luau front end refuses.</summary>
    public static readonly CookDiagnosticId ScriptSyntaxError = CookDiagnosticId.Cook(8001);

    /// <summary>The pack could not be written.</summary>
    public static readonly CookDiagnosticId PackWriteFailed = CookDiagnosticId.Cook(9001);

    /// <summary>Two assets claim one pack entry.</summary>
    public static readonly CookDiagnosticId PackEntryCollision = CookDiagnosticId.Cook(9002);

    /// <summary>The pack was refused: its header, its regions or its digest.</summary>
    public static readonly CookDiagnosticId PackNotMountable = CookDiagnosticId.Cook(9003);

    /// <summary>
    /// An entry is present and its payload does not decode. The digest can be
    /// valid when this fails.
    /// </summary>
    public static readonly CookDiagnosticId PackEntryUnreadable = CookDiagnosticId.Cook(9004);

    /// <summary>
    /// The entry table on disk is not strictly ascending by asset id, so a
    /// binary search over it misses entries.
    /// </summary>
    public static readonly CookDiagnosticId PackEntryTableUnsorted = CookDiagnosticId.Cook(9005);

    /// <summary>
    /// An entry could not be verified, for example one with no name-table record.
    /// </summary>
    public static readonly CookDiagnosticId PackEntryNotVerifiable = CookDiagnosticId.Cook(9006);

    /// <summary>
    /// Whether <paramref name="number"/> was retired. A retired code is never
    /// reused.
    /// </summary>
    // A switch, not a collection: no static state to initialise after the codes above.
    public static bool IsRetired(int number) => number switch
    {
        _ => false,
    };

    /// <summary>The subsystem a band names, for help text and for log lines.</summary>
    public static string DescribeBand(int band) => band switch
    {
        0 => "project and CLI",
        1 => "discovery and dependencies",
        2 => "image",
        3 => "model",
        4 => "audio",
        5 => "material",
        6 => "shader",
        7 => "map and geometry",
        8 => "script",
        9 => "pack writing",
        _ => "unknown",
    };
}
