using System;
using System.IO;

namespace Spectra.Kitchen.Models;

/// <summary>
/// A glTF or GLB file was refused. The message names what was refused.
/// </summary>
// IOException, because InvalidDataException is sealed.
public sealed class GltfFormatException : IOException
{
    /// <summary>Creates the exception.</summary>
    public GltfFormatException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with the underlying failure.</summary>
    public GltfFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
