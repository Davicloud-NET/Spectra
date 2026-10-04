using System;
using System.IO;

namespace SpectraEngine.Core.Assets.Audio;

/// <summary>
/// A <c>.saudio</c> file was refused at read.
/// </summary>
// IOException so generic content-load handlers catch it; InvalidDataException is sealed.
public sealed class SaudioFormatException : IOException
{
    /// <summary>Creates the exception.</summary>
    public SaudioFormatException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception, carrying what actually failed underneath.</summary>
    public SaudioFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
