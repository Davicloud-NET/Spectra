using System;

namespace SpectraEngine.Editing.Hosting;

/// <summary>How much of a brush one material assignment covers.</summary>
public enum MaterialDropScope
{
    /// <summary>The one face under the pointer. The default.</summary>
    Face,

    /// <summary>Every face of the brush under the pointer.</summary>
    Brush,
}

/// <summary>
/// What one material assignment did. Three outcomes: refused (nothing
/// happened), unresolved (faces were painted but the file is missing), and
/// applied, where zero faces changed means the brush already wore it.
/// </summary>
/// <param name="ContentPath">The material asked for, empty for the engine default.</param>
/// <param name="NodeId">What was painted, or <see cref="Guid.Empty"/> for nothing.</param>
/// <param name="FacesChanged">How many faces now wear a different material.</param>
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
