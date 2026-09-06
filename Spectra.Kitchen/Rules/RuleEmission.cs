using SpectraEngine.Core.Assets.Packs;
using Spectra.Kitchen.Packs;

namespace Spectra.Kitchen.Rules;

/// <summary>One cooked output a rule produced.</summary>
/// <param name="Path">Normalised content-relative path the engine resolves it by.</param>
/// <param name="Kind">What the payload is, carried into the pack entry.</param>
/// <param name="Payload">The cooked bytes, already copied out of the rule's buffer.</param>
public readonly record struct RuleEmission(string Path, PackEntryKind Kind, PackPayload Content)
{
    public RuleEmission(string Path, PackEntryKind Kind, byte[] Payload) : this(Path, Kind, PackPayload.FromBytes(Payload)) { }
    public byte[] Payload => Content.ReadAllBytes();
}
