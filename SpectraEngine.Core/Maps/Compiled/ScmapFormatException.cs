using System;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// A <c>.scmap</c> file is not one, is a version this engine does not read, or is
/// internally inconsistent.
/// </summary>
public sealed class ScmapFormatException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public ScmapFormatException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    public ScmapFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
