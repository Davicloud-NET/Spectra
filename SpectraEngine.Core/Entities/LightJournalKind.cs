using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Core.Entities;

// The kinds of entry the play journal holds for a light. The owner is the
// light and not its node: the value goes back on the object it was read
// from. Something new a level can change on a light is one more of these.
internal static class LightJournalKind
{
    public static readonly JournalKind<Light, bool> Enabled = new(
        static light => light.Enabled,
        static (light, first) => light.Enabled = first);

    public static readonly JournalKind<Light, float> Intensity = new(
        static light => light.Intensity,
        static (light, first) => light.Intensity = first);

    public static readonly JournalKind<Light, Vector3> Color = new(
        static light => light.Color,
        static (light, first) => light.Color = first);

    public static readonly JournalKind<Light, float> Range = new(
        static light => light.Range,
        static (light, first) => light.Range = first);
}
