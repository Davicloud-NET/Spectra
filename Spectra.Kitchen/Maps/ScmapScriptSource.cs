namespace Spectra.Kitchen.Maps;

/// <summary>One script on its way into an <c>SCPT</c> record.</summary>
/// <param name="NodeIndex">The <c>NODE</c> record of the node that is the script.</param>
/// <param name="Path">The script's file, as a content-relative path. It gets a row in <c>ASTB</c>.</param>
/// <param name="IsEnabled">Whether the script may run.</param>
/// <param name="IsModule">Whether it is a module.</param>
public readonly record struct ScmapScriptSource(int NodeIndex, string Path, bool IsEnabled = true, bool IsModule = false);
