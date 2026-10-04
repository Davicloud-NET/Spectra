using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Thrown when the graphics device is removed, reset or hung. The engine cannot
/// recreate a device mid-run, so this ends the run.
/// </summary>
public sealed class GraphicsDeviceLostException : Exception
{
    public GraphicsDeviceLostException(string message) : base(message)
    {
    }

    public GraphicsDeviceLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
