namespace SpectraEngine.Core.Serialization;

/// <summary>Raw JSON carried through a round trip untouched.</summary>
public sealed class PreservedValue
{
    public PreservedValue(byte[] raw) => Raw = raw;

    /// <summary>The value's bytes as they appeared in the source document.</summary>
    public byte[] Raw { get; }
}

/// <summary>
/// An unrecognised member, and where in the canonical member order it sat.
/// </summary>
// Anchor is the canonical index of the last known member before this one
// (-1 for none). Replaying unknowns at the end of the object instead would
// change the bytes when a newer engine interleaved its members with ours.
public sealed class PreservedMember
{
    public PreservedMember(string name, byte[] raw, int anchor)
    {
        Name = name;
        Raw = raw;
        Anchor = anchor;
    }

    public string Name { get; }

    public byte[] Raw { get; }

    public int Anchor { get; }
}
