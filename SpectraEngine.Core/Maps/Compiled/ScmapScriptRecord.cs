using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One 16-byte <c>SCPT</c> record: the script a node is. Records are in node
/// order, at most one per node. The script's text is not in the map: the
/// record names its file, which the pack holds as an entry of its own.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapScriptRecord
{
    /// <summary>The <see cref="KindRaw"/> of a script that runs when the level starts.</summary>
    public const byte ScriptKind = 0;

    /// <summary>The <see cref="KindRaw"/> of a module, which runs when a script asks for it.</summary>
    public const byte ModuleKind = 1;

    /// <summary>Bit 0 of <see cref="FlagsRaw"/>: the script is switched off.</summary>
    // Stored inverted, like the node flags: a record with no flag is a script that runs.
    public const byte DisabledFlag = 1 << 0;

    /// <summary>Index into <c>NODE</c> of the node this script is.</summary>
    public readonly uint NodeIndex;

    /// <summary>
    /// Index into <c>ASTB</c> of the script's file. That row is of kind
    /// <c>Script</c> and holds the content path.
    /// </summary>
    public readonly uint AssetIndex;

    /// <summary><see cref="ScriptKind"/> or <see cref="ModuleKind"/>.</summary>
    public readonly byte KindRaw;

    /// <summary>The flag bits. Only <see cref="DisabledFlag"/> is defined.</summary>
    public readonly byte FlagsRaw;

    /// <summary>Reserved; written zero.</summary>
    public readonly ushort Reserved0;

    /// <summary>Reserved; written zero.</summary>
    public readonly uint Reserved1;

    /// <summary>Builds one script record.</summary>
    public ScmapScriptRecord(uint nodeIndex, uint assetIndex, bool isEnabled, bool isModule)
    {
        NodeIndex = nodeIndex;
        AssetIndex = assetIndex;
        KindRaw = isModule ? ModuleKind : ScriptKind;
        FlagsRaw = isEnabled ? (byte)0 : DisabledFlag;
        Reserved0 = 0;
        Reserved1 = 0;
    }

    /// <summary>Whether the script may run.</summary>
    public bool IsEnabled => (FlagsRaw & DisabledFlag) == 0;

    /// <summary>Whether this is a module.</summary>
    public bool IsModule => KindRaw == ModuleKind;
}
