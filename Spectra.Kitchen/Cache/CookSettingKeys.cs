using System;

namespace Spectra.Kitchen.Cache;

/// <summary>
/// The cook settings a rule's output can depend on, declared per rule. Only the
/// declared ones enter that rule's cache key.
/// </summary>
// Only settings that change a cooked payload belong here. Jobs, Loose, the
// output paths, UseCache and Strict do not.
[Flags]
public enum CookSettingKeys
{
    /// <summary>The rule's output is the same under every setting.</summary>
    None = 0,

    /// <summary>Reads <c>CookSettings.Profile</c>: ship, fast or preview.</summary>
    Profile = 1 << 0,

    /// <summary>Reads <c>CookSettings.Targets</c>: which backends are cooked for.</summary>
    Targets = 1 << 1,

    /// <summary>Reads <c>CookSettings.ScriptSource</c>: embed or strip.</summary>
    ScriptSource = 1 << 2,

    /// <summary>Reads <c>CookSettings.Encoder</c>: managed or native.</summary>
    Encoder = 1 << 3,

    /// <summary>Reads <c>CookSettings.KeepBrushSource</c>.</summary>
    KeepBrushSource = 1 << 4,

    /// <summary>
    /// Reads <c>CookSettings.AudioSampleRate</c>: the one rate every cooked
    /// sound is resampled to.
    /// </summary>
    AudioSampleRate = 1 << 5,
}
