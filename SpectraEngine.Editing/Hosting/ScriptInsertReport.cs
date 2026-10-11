using SpectraEngine.Core.Assets;
using System;

namespace SpectraEngine.Editing.Hosting;

/// <summary>
/// What adding a script to the level did: a script node is in the scene and
/// the history, or <see cref="Refused"/> says why nothing is.
/// </summary>
/// <param name="ContentPath">The script's file, relative to the content root. Empty when none was made.</param>
/// <param name="NodeId">The node that was added, or <see cref="Guid.Empty"/> when nothing was.</param>
/// <param name="ParentName">The name of the node the script went under.</param>
/// <param name="Refused">Why nothing was added, as the end of a sentence, or null when something was.</param>
public readonly record struct ScriptInsertReport(
    string ContentPath,
    Guid NodeId,
    string ParentName,
    string? Refused)
{
    /// <summary>Whether the script went under the scene's root, so it belongs to the level as a whole.</summary>
    public bool IsLevelScript { get; init; }

    /// <summary>Whether the file was made for this script, as opposed to one that was there.</summary>
    public bool MadeFile { get; init; }

    /// <summary>Whether a node reached the scene.</summary>
    public bool Added => NodeId != Guid.Empty;

    /// <summary>A report for a request nothing acted on, naming why.</summary>
    /// <param name="contentPath">The file that was to be attached, or empty when one was to be made.</param>
    public static ScriptInsertReport RefusedBecause(string contentPath, string reason) =>
        new(contentPath, Guid.Empty, string.Empty, reason);

    /// <summary>One line for a status bar or an output log.</summary>
    public string Describe()
    {
        if (Refused is { } refused)
        {
            return ContentPath.Length == 0
                ? $"Insert script did nothing: {refused}"
                : $"{ContentPath} was not added: {refused}";
        }

        string where = IsLevelScript ? "a level script" : $"a script to '{ParentName}'";

        // The file outlives an undo, so say that one was made and where.
        if (MadeFile)
            return $"Added {where} and made its file, {ContentRoot.DirectoryName}/{ContentPath}.";

        return IsLevelScript
            ? $"{ContentPath} added as a level script."
            : $"{ContentPath} added to '{ParentName}' as a script.";
    }
}
