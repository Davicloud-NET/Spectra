using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// One level of detail, as its twelve bytes sit in a <c>LODS</c> section: a
/// range of submeshes over the model's shared vertex and index buffers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmodelLod
{
    /// <summary>
    /// The projected height, as a fraction of the viewport, below which this
    /// level is drawn.
    /// </summary>
    public readonly float ScreenHeightThreshold;

    /// <summary>Index of this level's first submesh in <c>SUBM</c>.</summary>
    public readonly uint FirstSubmesh;

    /// <summary>How many consecutive submeshes this level covers.</summary>
    public readonly uint SubmeshCount;

    /// <summary>Builds one level-of-detail record.</summary>
    public SmodelLod(float screenHeightThreshold, uint firstSubmesh, uint submeshCount)
    {
        ScreenHeightThreshold = screenHeightThreshold;
        FirstSubmesh = firstSubmesh;
        SubmeshCount = submeshCount;
    }
}
