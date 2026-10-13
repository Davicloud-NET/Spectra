using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Core.Scripting.Tooling;

/// <summary>What became of setting a project's folder up for a code editor.</summary>
/// <param name="Written">The files that were written, each by its path under the project folder.</param>
/// <param name="Refused">The files that could not be written.</param>
/// <param name="Reason">Why the first of those could not, as a sentence. Empty with none.</param>
public sealed record ScriptProjectReport(
    IReadOnlyList<string> Written, IReadOnlyList<string> Refused, string Reason)
{
    /// <summary>Nothing was written and nothing was refused.</summary>
    public static ScriptProjectReport Nothing { get; } = new([], [], string.Empty);

    /// <summary>Whether the definitions file was there and held another text.</summary>
    public bool IsRenewal { get; init; }

    /// <summary>Whether a file could not be written.</summary>
    public bool IsRefused => Refused.Count > 0;

    /// <summary>A report of a setup that failed as a whole.</summary>
    public static ScriptProjectReport Failed(string reason) =>
        new([], [ScriptProjectFiles.Definitions], Sentence(reason));

    /// <summary>What was written, as a sentence or two for a person. Null when nothing was.</summary>
    public string? DescribeWritten() => Written.Count switch
    {
        0 => null,
        1 when IsRenewal => $"Wrote {Written[0]} again. It did not hold this engine's script API.",
        1 => $"Wrote {Written[0]} into the project folder. It sets a code editor up for the project's scripts.",
        _ => $"Wrote {Listed(Written)} into the project folder. They set a code editor up for the project's scripts.",
    };

    /// <summary>What could not be written and why, for a person. Null when nothing was refused.</summary>
    public string? DescribeRefused() =>
        IsRefused ? $"Could not write {Listed(Refused)} into the project folder. {Reason}".TrimEnd() : null;

    /// <summary>Both in one line, or null when there is nothing to say.</summary>
    public string? Describe() => (DescribeWritten(), DescribeRefused()) switch
    {
        (null, null) => null,
        ({ } wrote, null) => wrote,
        (null, { } refused) => refused,
        ({ } wrote, { } refused) => wrote + " " + refused,
    };

    internal static string Sentence(string words)
    {
        string trimmed = words.Trim();
        return trimmed.Length == 0 || trimmed.EndsWith('.') ? trimmed : trimmed + ".";
    }

    // a, b and c
    private static string Listed(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };
}
