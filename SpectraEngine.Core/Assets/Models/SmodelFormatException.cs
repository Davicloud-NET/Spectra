using System;
using System.IO;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// A <c>.smodel</c> file was refused at read.
/// </summary>
// IOException so hosts that catch failed content loads generically also catch
// this. InvalidDataException is sealed.
public sealed class SmodelFormatException : IOException
{
    /// <summary>Creates the exception.</summary>
    public SmodelFormatException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with the underlying failure.</summary>
    public SmodelFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
