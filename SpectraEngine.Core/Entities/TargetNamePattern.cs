using System;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// How a target name matches entity names: the whole name, or a prefix when
/// it ends in <c>*</c>. Ordinal. This is the rule a wire resolves by, so
/// anything that asks what a name would reach should ask here.
/// </summary>
public static class TargetNamePattern
{
    private const char Wildcard = '*';

    /// <summary>
    /// Whether <paramref name="pattern"/> is <c>!self</c>, <c>!activator</c>
    /// or <c>!caller</c>: an entity picked when an output fires, not a name.
    /// </summary>
    public static bool IsRuntimeToken(string? pattern) =>
        pattern is TargetNameIndex.SelfToken
            or TargetNameIndex.ActivatorToken
            or TargetNameIndex.CallerToken;

    /// <summary>Whether <paramref name="pattern"/> matches by prefix and so can match several names.</summary>
    public static bool IsPrefix(string? pattern) =>
        !string.IsNullOrEmpty(pattern) && pattern[0] != '!' && pattern[^1] == Wildcard;

    /// <summary>
    /// Whether <paramref name="pattern"/> matches <paramref name="name"/>. An
    /// empty pattern matches nothing, and neither does one that starts with
    /// <c>!</c>, even when an entity has that name.
    /// </summary>
    public static bool Matches(string? pattern, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (string.IsNullOrEmpty(pattern) || pattern[0] == '!')
            return false;

        if (pattern[^1] == Wildcard)
            return name.AsSpan().StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal);

        return string.Equals(pattern, name, StringComparison.Ordinal);
    }
}
