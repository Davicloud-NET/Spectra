using System;
using System.IO;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// A <c>.spack</c> file was refused at mount. Mounting is the only pack operation
/// that throws; lookups after it report a miss instead.
/// </summary>
public sealed class PackMountException : IOException
{
    /// <summary>Creates the exception.</summary>
    public PackMountException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with the underlying failure.</summary>
    public PackMountException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
