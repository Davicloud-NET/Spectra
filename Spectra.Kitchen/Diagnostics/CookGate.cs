using System;

namespace Spectra.Kitchen.Diagnostics;

/// <summary>
/// What the gate decided one diagnostic code is worth.
/// </summary>
public enum CookGateVerdict
{
    /// <summary>Info, always. <c>--strict</c> does not move it.</summary>
    Note,

    /// <summary>
    /// Warning, always. About the run rather than the data (a cache that would
    /// not save, a switch not wired up), so <c>--strict</c> does not move it.
    /// </summary>
    Warning,

    /// <summary>Warning by default, error under <c>--strict</c>.</summary>
    WarningUnlessStrict,

    /// <summary>Error, always. A pack carrying this is refused.</summary>
    Fatal,

    /// <summary>
    /// The reporter's severity stands. For codes that carry another tool's
    /// judgement, such as a shader compiler diagnostic. <c>--strict</c> still
    /// promotes a warning.
    /// </summary>
    AsReported,
}

/// <summary>
/// Decides what a cook diagnostic costs: which codes refuse the pack, which
/// warn, and which <c>--strict</c> may promote. Read by the cook and the verifier.
/// </summary>
// The runtime degrades on a missing texture so a frame keeps rendering. The
// cook refuses the same thing. Both are intended; don't make one match the other.
// The severity a reporting site chose is overridden by this table.
public static class CookGate
{
    /// <summary>
    /// What the gate decided <paramref name="id"/> is worth. An unclassified
    /// code is <see cref="CookGateVerdict.Fatal"/>.
    /// </summary>
    public static CookGateVerdict Verdict(CookDiagnosticId id)
    {
        // A wrapped code keeps the severity its own tool gave it.
        if (!id.IsCookCode) return CookGateVerdict.AsReported;

        // Fatal by default: a forgotten code must not let bad data ship.
        return Classify(id.Number) ?? CookGateVerdict.Fatal;
    }

    /// <summary>
    /// Whether this table classifies <paramref name="id"/>. For the convention test.
    /// </summary>
    public static bool IsClassified(CookDiagnosticId id) =>
        id.IsCookCode && Classify(id.Number) is not null;

    /// <summary>Whether a pack carrying <paramref name="id"/> is refused.</summary>
    public static bool IsFatal(CookDiagnosticId id) => Verdict(id) == CookGateVerdict.Fatal;

    /// <summary>
    /// <paramref name="diagnostic"/> at the severity the gate decided, given
    /// whether this run is <c>--strict</c>.
    /// </summary>
    public static CookDiagnostic Apply(CookDiagnostic diagnostic, bool strict)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        CookDiagnosticSeverity decided = Verdict(diagnostic.Id) switch
        {
            CookGateVerdict.Note => CookDiagnosticSeverity.Info,
            CookGateVerdict.Warning => CookDiagnosticSeverity.Warning,
            CookGateVerdict.WarningUnlessStrict =>
                strict ? CookDiagnosticSeverity.Error : CookDiagnosticSeverity.Warning,
            CookGateVerdict.Fatal => CookDiagnosticSeverity.Error,

            // AsReported
            _ => strict && diagnostic.Severity == CookDiagnosticSeverity.Warning
                ? CookDiagnosticSeverity.Error
                : diagnostic.Severity,
        };

        return diagnostic.Severity == decided ? diagnostic : diagnostic with { Severity = decided };
    }

    // Literal numbers: the CookDiagnosticCodes members are static readonly, not
    // constants. CookGateTests checks every declared code appears here.
    private static CookGateVerdict? Classify(int number) => number switch
    {
        1 => CookGateVerdict.Fatal,                // SC0001 ProjectNotOpened
        2 => CookGateVerdict.Fatal,                // SC0002 VerbNotImplemented
        3 => CookGateVerdict.Warning,              // SC0003 OptionNotImplemented
        4 => CookGateVerdict.Fatal,                // SC0004 OutputNotWritable
        5 => CookGateVerdict.Fatal,                // SC0005 UnsafeCleanTarget

        1001 => CookGateVerdict.Fatal,             // SC1001 ContentRootMissing
        1002 => CookGateVerdict.Fatal,             // SC1002 InputMissing
        1003 => CookGateVerdict.Fatal,             // SC1003 InputPathInvalid
        1004 => CookGateVerdict.Fatal,             // SC1004 RuleFailed
        1005 => CookGateVerdict.Note,              // SC1005 ContentNotCooked
        1006 => CookGateVerdict.Warning,           // SC1006 CacheNotWritable
        1007 => CookGateVerdict.Note,              // SC1007 CacheDiscarded

        2001 => CookGateVerdict.Fatal,             // SC2001 ImageUndecodable
        2002 => CookGateVerdict.Fatal,             // SC2002 ImageEncodeFailed
        2003 => CookGateVerdict.Fatal,             // SC2003 ImageFileUnreadable

        3001 => CookGateVerdict.Fatal,             // SC3001 ModelUndecodable
        3002 => CookGateVerdict.WarningUnlessStrict, // SC3002 ModelMaterialUnauthored
        3003 => CookGateVerdict.Fatal,             // SC3003 ModelEncodeFailed
        3004 => CookGateVerdict.Note,              // SC3004 ModelDataDropped
        3005 => CookGateVerdict.Fatal,             // SC3005 ModelFileUnreadable
        3006 => CookGateVerdict.Fatal,             // SC3006 ModelMaterialMissing

        4001 => CookGateVerdict.Fatal,             // SC4001 AudioUndecodable
        4002 => CookGateVerdict.Fatal,             // SC4002 AudioEncodeFailed
        4003 => CookGateVerdict.WarningUnlessStrict, // SC4003 AudioStereoPositional
        4004 => CookGateVerdict.Note,              // SC4004 AudioResampled
        4005 => CookGateVerdict.WarningUnlessStrict, // SC4005 AudioLoopUnusable
        4006 => CookGateVerdict.Fatal,             // SC4006 AudioFileUnreadable
        4007 => CookGateVerdict.WarningUnlessStrict, // SC4007 AudioMarkerPastEnd
        4008 => CookGateVerdict.WarningUnlessStrict, // SC4008 AudioMarkerLabelUnreadable
        4009 => CookGateVerdict.WarningUnlessStrict, // SC4009 AudioMarkerLabelFileUnused
        4010 => CookGateVerdict.WarningUnlessStrict, // SC4010 AudioMarkerNameTaken

        4101 => CookGateVerdict.WarningUnlessStrict, // SC4101 CaptionLineUnreadable
        4102 => CookGateVerdict.WarningUnlessStrict, // SC4102 CaptionSoundRepeated
        4103 => CookGateVerdict.WarningUnlessStrict, // SC4103 CaptionSoundMissing
        4104 => CookGateVerdict.WarningUnlessStrict, // SC4104 CaptionHiddenBySubtitles
        4105 => CookGateVerdict.WarningUnlessStrict, // SC4105 CaptionFileNotALanguage
        4106 => CookGateVerdict.WarningUnlessStrict, // SC4106 CaptionLanguageIncomplete
        4107 => CookGateVerdict.Note,              // SC4107 CaptionLanguageSummary
        4108 => CookGateVerdict.Fatal,             // SC4108 SubtitleUnreadable
        4109 => CookGateVerdict.WarningUnlessStrict, // SC4109 SubtitlePartNotRead
        4110 => CookGateVerdict.WarningUnlessStrict, // SC4110 SubtitleStartsAfterSound
        4111 => CookGateVerdict.WarningUnlessStrict, // SC4111 SubtitleNameHasNoLanguage
        4112 => CookGateVerdict.WarningUnlessStrict, // SC4112 SubtitleHasNoCues

        5001 => CookGateVerdict.Fatal,             // SC5001 MaterialTextureMissing
        5002 => CookGateVerdict.WarningUnlessStrict, // SC5002 MaterialFileMalformed
        5003 => CookGateVerdict.Fatal,             // SC5003 MaterialShaderMissing

        6001 => CookGateVerdict.AsReported,        // SC6001 ShaderCompileFailed
        6002 => CookGateVerdict.Fatal,             // SC6002 ShaderBackendMissing
        6003 => CookGateVerdict.Fatal,             // SC6003 ShaderFileUnreadable
        6004 => CookGateVerdict.Fatal,             // SC6004 ShaderBackendUnsupported
        6005 => CookGateVerdict.WarningUnlessStrict, // SC6005 ShaderNoTargets

        7001 => CookGateVerdict.Fatal,             // SC7001 MapBrushNonRigid
        7002 => CookGateVerdict.Fatal,             // SC7002 MapBrushRefused
        7003 => CookGateVerdict.Fatal,             // SC7003 MapNodeIdDuplicate
        7004 => CookGateVerdict.Fatal,             // SC7004 MapFaceCountMismatch
        7005 => CookGateVerdict.WarningUnlessStrict, // SC7005 MapConnectionTargetMissing
        7006 => CookGateVerdict.WarningUnlessStrict, // SC7006 MapEntityClassUnknown
        7007 => CookGateVerdict.Fatal,             // SC7007 MapDocumentMalformed
        7008 => CookGateVerdict.Fatal,             // SC7008 MapAssetMissing
        7009 => CookGateVerdict.Fatal,             // SC7009 MapFileUnreadable
        7010 => CookGateVerdict.WarningUnlessStrict, // SC7010 MapEntityOnWorldBrush

        8001 => CookGateVerdict.Fatal,             // SC8001 ScriptSyntaxError

        9001 => CookGateVerdict.Fatal,             // SC9001 PackWriteFailed
        9002 => CookGateVerdict.Fatal,             // SC9002 PackEntryCollision
        9003 => CookGateVerdict.Fatal,             // SC9003 PackNotMountable
        9004 => CookGateVerdict.Fatal,             // SC9004 PackEntryUnreadable
        9005 => CookGateVerdict.Fatal,             // SC9005 PackEntryTableUnsorted
        9006 => CookGateVerdict.WarningUnlessStrict, // SC9006 PackEntryNotVerifiable

        _ => null,
    };
}
