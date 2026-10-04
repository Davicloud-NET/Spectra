using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Viewport;

/// <summary>
/// Which viewport a session asks for. <see cref="Auto"/> is the default and
/// stays native until this machine has enough green composited sessions.
/// </summary>
public enum ViewportMode
{
    /// <summary>The Win32 child window. Composites above everything Avalonia draws.</summary>
    Native,

    /// <summary>The compositor-imported shared texture. Ends airspace.</summary>
    Composition,

    /// <summary>Let <see cref="ViewportModePolicy"/> decide from the recorded history.</summary>
    Auto,
}

/// <summary>
/// Why a session got the viewport it got, whichever way the choice went.
/// </summary>
// Every value needs a sentence in ViewportModePolicy.Describe; a test checks.
// Decide never returns FirstUpdateFaulted: a live session reports it.
public enum ViewportChoiceReason
{
    /// <summary>Composition, because the command line asked for it and the machine can.</summary>
    ExplicitComposition,

    /// <summary>Composition, because this machine has earned it. See <see cref="ViewportModePolicy.RequiredGreenSessions"/>.</summary>
    ProvenByHistory,

    /// <summary>Native, because the command line said so. This beats any history.</summary>
    ExplicitNative,

    /// <summary>Native, because the run of green sessions on this machine is not long enough yet.</summary>
    NotYetProven,

    /// <summary>Native, because the history was recorded against a different GPU.</summary>
    AdapterChanged,

    /// <summary>Native, because the history was recorded against a different driver build.</summary>
    DriverChanged,

    /// <summary>Native, because an embedded GL surface needs a WGL context that does not exist.</summary>
    BackendIsOpenGl,

    /// <summary>Native, because this window has no compositor to hand a frame to.</summary>
    NoCompositor,

    /// <summary>Native, because the compositor exposes no GPU interop.</summary>
    NoGpuInterop,

    /// <summary>Native, because the compositor imports no handle kind the engine produces.</summary>
    HandleKindUnsupported,

    /// <summary>Native, because the compositor cannot synchronise that handle with a keyed mutex.</summary>
    NoKeyedMutexSync,

    /// <summary>Native, because the one-texel rehearsal import was refused.</summary>
    DryRunImportFailed,

    /// <summary>
    /// A live composited session's hand-over stopped. Reported only; the
    /// viewport is not swapped mid-session.
    /// </summary>
    FirstUpdateFaulted,
}

/// <summary>
/// The persisted half of the viewport decision: what was asked for, and what
/// this machine has earned.
/// </summary>
/// <param name="Mode">What the user or the command line asked for.</param>
/// <param name="GreenSessions">Consecutive green composited sessions on this machine.</param>
/// <param name="AdapterLuid">The adapter the count was earned on, or empty before the first one.</param>
/// <param name="DriverVersion">The driver build the count was earned on, or empty.</param>
public readonly record struct ViewportPreference(
    ViewportMode Mode,
    int GreenSessions,
    string AdapterLuid,
    string DriverVersion)
{
    /// <summary>A machine that has never run a composited session, on the default mode.</summary>
    public static ViewportPreference Default { get; } =
        new(ViewportMode.Auto, GreenSessions: 0, AdapterLuid: string.Empty, DriverVersion: string.Empty);
}

/// <summary>
/// What this machine can do for a composited viewport, as measured.
/// </summary>
// A compositor can advertise a handle kind it cannot synchronise, or advertise
// both and still refuse the import, so each is its own field.
// CompareGreen is the last --viewport-compare verdict: a double sRGB encode
// raises no error anywhere else.
public readonly record struct ViewportCapabilities(
    bool HasCompositor,
    bool HasGpuInterop,
    bool SupportsD3D11NtHandle,
    bool SupportsKeyedMutex,
    bool DryRunImported,
    bool CompareGreen,
    string AdapterLuid,
    string AdapterName,
    string DriverVersion)
{
    /// <summary>
    /// Nothing was measured. For <see cref="ViewportModePolicy.Decide"/> when
    /// the preference alone answers.
    /// </summary>
    public static ViewportCapabilities NotMeasured { get; } = new(
        HasCompositor: false,
        HasGpuInterop: false,
        SupportsD3D11NtHandle: false,
        SupportsKeyedMutex: false,
        DryRunImported: false,
        CompareGreen: false,
        AdapterLuid: string.Empty,
        AdapterName: string.Empty,
        DriverVersion: string.Empty);

    /// <summary>Everything the composited path needs, for a test that is about the preference.</summary>
    public static ViewportCapabilities Ideal { get; } = new(
        HasCompositor: true,
        HasGpuInterop: true,
        SupportsD3D11NtHandle: true,
        SupportsKeyedMutex: true,
        DryRunImported: true,
        CompareGreen: true,
        AdapterLuid: "9a91010000000000",
        AdapterName: "Test Adapter",
        DriverVersion: "31.0.101.5085");
}

/// <summary>
/// The chosen viewport, the reason, and the sentence that explains it.
/// <see cref="Explanation"/> is never empty.
/// </summary>
public readonly record struct ViewportDecision(
    bool UseComposition,
    ViewportChoiceReason Reason,
    string Explanation);

/// <summary>
/// Decides which viewport a session gets and always gives a reason. Pure: no
/// I/O, no Avalonia type, no GPU.
/// </summary>
public static class ViewportModePolicy
{
    /// <summary>
    /// How many consecutive green composited sessions a machine owes before
    /// <see cref="ViewportMode.Auto"/> will choose composition on it.
    /// </summary>
    // More than one because the failures are intermittent: an import that
    // fails after sleep, a hybrid laptop switching GPU.
    public const int RequiredGreenSessions = 5;

    /// <summary>
    /// Whether the machine has to be measured before <see cref="Decide"/> can
    /// answer.
    /// </summary>
    // Measuring opens a graphics device, so skip it when the answer cannot
    // depend on it.
    public static bool RequiresMeasurement(ViewportPreference settings, GraphicsBackend backend) =>
        PreferenceVerdict(settings, backend) is null;

    /// <summary>
    /// Chooses the viewport for one session.
    /// </summary>
    /// <param name="settings">The persisted preference and history.</param>
    /// <param name="capabilities">
    /// What was measured, or <see cref="ViewportCapabilities.NotMeasured"/> when
    /// <see cref="RequiresMeasurement"/> said none was needed.
    /// </param>
    /// <param name="backend">The graphics backend this session will run on.</param>
    public static ViewportDecision Decide(
        ViewportPreference settings, ViewportCapabilities capabilities, GraphicsBackend backend)
    {
        if (PreferenceVerdict(settings, backend) is { } refused)
            return Fallback(refused);

        // Most fundamental first, so the reported reason is the deepest one.
        if (!capabilities.HasCompositor)
            return Fallback(ViewportChoiceReason.NoCompositor);

        if (!capabilities.HasGpuInterop)
            return Fallback(ViewportChoiceReason.NoGpuInterop);

        if (!capabilities.SupportsD3D11NtHandle)
            return Fallback(ViewportChoiceReason.HandleKindUnsupported);

        if (!capabilities.SupportsKeyedMutex)
            return Fallback(ViewportChoiceReason.NoKeyedMutexSync);

        if (!capabilities.DryRunImported)
            return Fallback(ViewportChoiceReason.DryRunImportFailed);

        if (settings.Mode is ViewportMode.Composition)
            return Chosen(ViewportChoiceReason.ExplicitComposition);

        // Auto only. The adapter and driver are known only after measuring.
        if (!Same(settings.AdapterLuid, capabilities.AdapterLuid))
            return Fallback(ViewportChoiceReason.AdapterChanged);

        if (!Same(settings.DriverVersion, capabilities.DriverVersion))
            return Fallback(ViewportChoiceReason.DriverChanged);

        return Chosen(ViewportChoiceReason.ProvenByHistory);
    }

    // The part of the decision that needs no measurement. Null: measure.
    // OpenGL is refused first whatever was asked: there is no embedded WGL
    // context. Explicit native beats a green history.
    private static ViewportChoiceReason? PreferenceVerdict(
        ViewportPreference settings, GraphicsBackend backend)
    {
        if (backend is GraphicsBackend.OpenGL)
            return ViewportChoiceReason.BackendIsOpenGl;

        if (settings.Mode is ViewportMode.Native)
            return ViewportChoiceReason.ExplicitNative;

        if (settings.Mode is ViewportMode.Composition)
            return null;

        // Auto. Whether the count was earned on this machine is checked in
        // Decide, after measuring.
        if (settings.GreenSessions < RequiredGreenSessions)
            return ViewportChoiceReason.NotYetProven;

        return null;
    }

    /// <summary>
    /// Re-anchors the history on the measured adapter and driver, zeroing the
    /// count if either changed. Call only when the machine was measured.
    /// </summary>
    public static ViewportPreference Rebase(
        ViewportPreference settings, string adapterLuid, string driverVersion)
    {
        ArgumentNullException.ThrowIfNull(adapterLuid);
        ArgumentNullException.ThrowIfNull(driverVersion);

        bool sameMachine = Same(settings.AdapterLuid, adapterLuid)
            && Same(settings.DriverVersion, driverVersion);

        return settings with
        {
            GreenSessions = sameMachine ? settings.GreenSessions : 0,
            AdapterLuid = adapterLuid,
            DriverVersion = driverVersion,
        };
    }

    /// <summary>
    /// Folds one finished composited session into the history: one longer if it
    /// was green, back to zero if not. Do not record a native session.
    /// </summary>
    public static ViewportPreference Record(ViewportPreference settings, bool sessionGreen) =>
        settings with
        {
            GreenSessions = sessionGreen
                ? Math.Min(settings.GreenSessions + 1, RequiredGreenSessions)
                : 0,
        };

    /// <summary>
    /// Whether a finished composited session counts toward the flip.
    /// </summary>
    // compareGreen is needed because a double sRGB encode raises no
    // debug-layer error and no fault.
    // debugLayerErrors is the renderer's counted total, which already leaves
    // out the forgiven D3D12 bridge-wrap message.
    public static bool IsSessionGreen(int debugLayerErrors, bool faulted, bool compareGreen) =>
        debugLayerErrors == 0 && !faulted && compareGreen;

    /// <summary>The sentence behind a reason. Never empty, for any value.</summary>
    public static string Describe(ViewportChoiceReason reason) => reason switch
    {
        ViewportChoiceReason.ExplicitComposition =>
            "the command line asked for the composited viewport and this machine can host it",

        ViewportChoiceReason.ProvenByHistory =>
            $"this adapter and driver have produced {RequiredGreenSessions} consecutive green composited " +
            "sessions, so auto chose composition",

        ViewportChoiceReason.ExplicitNative =>
            "the command line asked for the native child window, which beats any recorded history",

        ViewportChoiceReason.NotYetProven =>
            $"auto keeps the native child until this adapter and driver have produced " +
            $"{RequiredGreenSessions} consecutive green composited sessions",

        ViewportChoiceReason.AdapterChanged =>
            "the recorded composited sessions were earned on a different adapter, so the count starts again",

        ViewportChoiceReason.DriverChanged =>
            "the recorded composited sessions were earned on a different driver build, so the count starts " +
            "again",

        ViewportChoiceReason.BackendIsOpenGl =>
            "an embedded OpenGL surface needs its own WGL context and proc-address loader, which is not " +
            "built, so composited OpenGL is refused by name rather than attempted",

        ViewportChoiceReason.NoCompositor =>
            "this window has no compositor, so an engine frame has nowhere to go",

        ViewportChoiceReason.NoGpuInterop =>
            "this compositor exposes no GPU interop, so an engine frame cannot be imported",

        ViewportChoiceReason.HandleKindUnsupported =>
            "this compositor does not import D3D11 NT handles, which is the only kind the engine produces",

        ViewportChoiceReason.NoKeyedMutexSync =>
            "this compositor imports the handle but cannot synchronise it with a keyed mutex, which is the " +
            "only hand-over the engine implements",

        ViewportChoiceReason.DryRunImportFailed =>
            "the one-texel rehearsal import was refused, so a real frame would have been refused too - and " +
            "after an engine was already running against it",

        ViewportChoiceReason.FirstUpdateFaulted =>
            "the composited hand-over faulted while the session was running; relaunch with --viewport=native",

        _ => throw new ArgumentOutOfRangeException(
            nameof(reason), reason, "Every viewport choice reason owes a sentence."),
    };

    /// <summary>The switch that names the viewport for one run.</summary>
    public const string Switch = "--viewport=";

    /// <summary>The switch's usage text, for messages.</summary>
    public const string Usage = "--viewport=composition|native|auto";

    /// <summary>
    /// Reads <c>--viewport=</c> off a command line. Null means it was not
    /// given, which leaves the saved setting alone.
    /// </summary>
    public static ViewportMode? RequestedMode(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        ViewportMode? found = null;

        // Last one wins, so a wrapper script's default can be overridden.
        foreach (string arg in args)
        {
            if (!arg.StartsWith(Switch, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryParseMode(arg[Switch.Length..], out ViewportMode mode))
                found = mode;
        }

        return found;
    }

    /// <summary>Parses one mode word.</summary>
    // Not Enum.Parse: trimming removes enum name reflection.
    public static bool TryParseMode(string? text, out ViewportMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "native":
                mode = ViewportMode.Native;
                return true;

            case "composition" or "composited":
                mode = ViewportMode.Composition;
                return true;

            case "auto":
                mode = ViewportMode.Auto;
                return true;

            default:
                mode = ViewportMode.Auto;
                return false;
        }
    }

    /// <summary>The word a mode is written as, in the settings file and in a log line.</summary>
    public static string NameOf(ViewportMode mode) => mode switch
    {
        ViewportMode.Native => "native",
        ViewportMode.Composition => "composition",
        ViewportMode.Auto => "auto",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown viewport mode."),
    };

    private static ViewportDecision Chosen(ViewportChoiceReason reason) =>
        new(UseComposition: true, reason, Describe(reason));

    private static ViewportDecision Fallback(ViewportChoiceReason reason) =>
        new(UseComposition: false, reason, Describe(reason));

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
