using System;

namespace Spectra.Kitchen.Rules;

/// <summary>How a rule touched a path.</summary>
public enum RuleDependencyKind
{
    /// <summary>The contents were read, so they are part of the key.</summary>
    Read,

    /// <summary>Probed and found. Only existence was seen, so no content hash.</summary>
    ProbeFound,

    /// <summary>
    /// Probed or read and not found. Adding the file later invalidates the rule
    /// that looked for it.
    /// </summary>
    ProbeMissing,
}

/// <summary>One path a rule touched, and what it saw there.</summary>
/// <param name="Path">Normalised content-relative path.</param>
/// <param name="Kind">What was seen.</param>
/// <param name="ContentHash">
/// <c>XxHash128</c> of the bytes for a <see cref="RuleDependencyKind.Read"/>, zero for a probe.
/// </param>
public readonly record struct RuleDependency(string Path, RuleDependencyKind Kind, UInt128 ContentHash)
{
    /// <summary>Whether the path was absent when the rule looked.</summary>
    public bool IsMissing => Kind == RuleDependencyKind.ProbeMissing;
}
