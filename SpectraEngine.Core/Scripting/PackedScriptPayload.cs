namespace SpectraEngine.Core.Scripting;

/// <summary>
/// What follows the header of a packed script, as one byte of that header.
/// </summary>
// The numbers are stored in packs: append only.
public enum PackedScriptPayload : byte
{
    /// <summary>The script's text as UTF-8, as its file holds it.</summary>
    Source = 1,
}
