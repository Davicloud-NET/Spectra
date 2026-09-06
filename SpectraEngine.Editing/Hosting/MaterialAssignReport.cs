using System;

namespace SpectraEngine.Editing.Hosting;

/// <summary>How much of a brush one material assignment covers.</summary>
/// <remarks>
/// <b>The face is the default and the block is the modifier, because painting
/// one wall of a room is what people do and painting a whole box is the
/// occasional shortcut.</b> The two are one gesture with a held key rather than
/// two gestures, so the scope is read at the drop and shown in the prompt
/// before it: a modifier whose effect is only visible afterwards is a modifier
/// nobody trusts.
/// </remarks>
public enum MaterialDropScope
{
    /// <summary>The one face under the pointer.</summary>
    Face,

    /// <summary>Every face of the brush under the pointer.</summary>
    Brush,
}

/// <summary>
/// What one material assignment did: how much of what it painted, and why it
/// painted nothing when it painted nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three outcomes rather than two, and a caller must not flatten them.</b> A
/// refusal means nothing happened and the gesture is worth repeating; an
/// unresolved material means the faces really were painted and the file behind
/// the name is missing, so the answer is to fix the asset rather than to press
/// Ctrl+Z; and a zero-face success means the brush already wore it, which is
/// neither a failure nor a change and records no history entry.
/// </para>
/// <para>
/// Built on the render thread, read wherever the caller marshalled to. Every
/// member is a value, exactly as <see cref="ModelInsertReport"/>'s are.
/// </para>
/// </remarks>
/// <param name="ContentPath">The material asked for, empty for the engine default.</param>
/// <param name="NodeId">What was painted, or <see cref="Guid.Empty"/> for nothing.</param>
/// <param name="NodeName">Its name, for a sentence that names what changed.</param>
/// <param name="FacesChanged">How many faces now wear a different material.</param>
/// <param name="FaceCount">How many faces the painted brush has.</param>
/// <param name="Refused">Why nothing was painted, or null.</param>
/// <param name="Unresolved">Why the painted faces will draw the default, or null.</param>
public readonly record struct MaterialAssignReport(
    string ContentPath,
    Guid NodeId,
    string NodeName,
    int FacesChanged,
    int FaceCount,
    string? Refused,
    string? Unresolved)
{
    /// <summary>Whether anything was painted.</summary>
    public bool Applied => Refused is null;

    /// <summary>A report for an assignment nothing acted on, naming why.</summary>
    public static MaterialAssignReport RefusedBecause(string contentPath, string reason) =>
        new(contentPath, Guid.Empty, string.Empty, 0, 0, reason, null);

    /// <summary>One line for a status bar or an output log.</summary>
    public string Describe()
    {
        string what = ContentPath.Length == 0 ? "The default material" : ContentPath;

        if (Refused is { } refused)
            return $"{what} was not applied: {refused}.";

        if (FacesChanged == 0)
            return $"'{NodeName}' already wears {what}.";

        string where = FaceCount > 0 && FacesChanged == FaceCount
            ? "the whole block"
            : FacesChanged == 1 ? "1 face" : $"{FacesChanged} faces";

        return Unresolved is { } unresolved
            ? $"{what} applied to {where} of '{NodeName}', but the file is missing: those faces draw the default material."
            : $"{what} applied to {where} of '{NodeName}'.";
    }
}
