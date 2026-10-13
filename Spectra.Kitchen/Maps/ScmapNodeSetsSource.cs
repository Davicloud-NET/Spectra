using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Maps;

/// <summary>
/// The tags and attributes one node was built with, on their way into
/// <c>NTAG</c> and <c>NATR</c> records.
/// </summary>
/// <param name="NodeIndex">The <c>NODE</c> record they belong to.</param>
public readonly record struct ScmapNodeSetsSource(int NodeIndex, TagSet Tags, AttributeSet Attributes);
