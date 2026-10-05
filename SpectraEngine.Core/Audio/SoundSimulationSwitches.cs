using SpectraEngine.Core.Audio.Propagation;
using System;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// What the engine works out for every sound: the switches a sound has, for
/// all sounds at once, and how strong Doppler is. A part is heard when the
/// engine's switch and the sound's own are both on. Render thread only.
/// </summary>
public sealed class SoundSimulationSwitches
{
    /// <summary>The strongest Doppler can be set.</summary>
    public const float MaxDopplerStrength = 4f;

    private float _dopplerStrength = 1f;

    /// <summary>The parts that are on for the whole engine. All of them, until one is switched off.</summary>
    public SoundSimulation Enabled { get; set; } = SoundSimulation.All;

    /// <summary>
    /// How much of the real Doppler shift is heard, from 0 to
    /// <see cref="MaxDopplerStrength"/>: 1 is real, 0 is none and 2 doubles
    /// it, counted in cents.
    /// </summary>
    public float DopplerStrength
    {
        get => _dopplerStrength;
        set
        {
            // Written so NaN is refused too.
            if (!(value >= 0f && value <= MaxDopplerStrength))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, $"A Doppler strength is a number from 0 to {MaxDopplerStrength}.");
            }

            _dopplerStrength = value;
        }
    }

    /// <summary>Switches parts on or off for the whole engine.</summary>
    public void Set(SoundSimulation parts, bool isOn) => Enabled = isOn ? Enabled | parts : Enabled & ~parts;

    /// <summary>What is worked out for a sound that has <paramref name="own"/> switched on.</summary>
    public SoundSimulation For(SoundSimulation own) => own & Enabled;
}
