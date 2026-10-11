using SpectraEngine.Core.Serialization;
using System.Collections.Generic;

namespace SpectraEngine.Core.Maps;

/// <summary>
/// A script payload: the <c>.luau</c> file a node's script names, and two
/// switches. An open record.
/// </summary>
// No source inside the map in this version. The reader refuses a 'source'
// member by name, so a save cannot drop one.
public sealed class MapScript
{
    // Append only, as on MapLight.
    internal static readonly string[] MemberOrder =
        [MapFormat.PathMember, MapFormat.EnabledMember, MapFormat.ModuleMember];

    /// <summary>Content-root-relative path of the script's file, as the file spells it.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Whether the script may run. Written only when false.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the script is a module, which runs when another script asks
    /// for it. Written only when true.
    /// </summary>
    public bool Module { get; set; }

    public List<PreservedMember> Unknown { get; } = [];
}
