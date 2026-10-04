using System;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// A rule read a path with nothing at it. The session reports it as <c>SC1002</c>
/// against the rule's source file.
/// </summary>
public sealed class RuleInputMissingException : Exception
{
    /// <summary>Creates the exception for <paramref name="contentPath"/>.</summary>
    public RuleInputMissingException(string contentPath, string sourcePath)
        : base($"Cooking '{sourcePath}' needs '{contentPath}', which is not in the content root.")
    {
        ContentPath = contentPath;
        SourcePath = sourcePath;
    }

    /// <summary>The content-relative path that was not there.</summary>
    public string ContentPath { get; }

    /// <summary>The asset being cooked when the read failed.</summary>
    public string SourcePath { get; }
}
