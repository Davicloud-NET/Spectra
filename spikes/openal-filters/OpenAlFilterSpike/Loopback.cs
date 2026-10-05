using Silk.NET.OpenAL;

namespace OpenAlFilterSpike;

/// <summary>
/// The ALC_SOFT_loopback entry points, fetched with alcGetProcAddress. No
/// device has to be open.
/// </summary>
internal sealed unsafe class Loopback
{
    // Values from OpenAL Soft 1.23.1, include/AL/alext.h, except Frequency,
    // which is core ALC from include/AL/alc.h.
    internal const string ExtensionName = "ALC_SOFT_loopback";
    internal const int Frequency = 0x1007;          // ALC_FREQUENCY
    internal const int FormatChannels = 0x1990;     // ALC_FORMAT_CHANNELS_SOFT
    internal const int FormatType = 0x1991;         // ALC_FORMAT_TYPE_SOFT
    internal const int Short = 0x1402;              // ALC_SHORT_SOFT
    internal const int Float = 0x1406;              // ALC_FLOAT_SOFT
    internal const int Mono = 0x1500;               // ALC_MONO_SOFT
    internal const int Stereo = 0x1501;             // ALC_STEREO_SOFT
    internal const int Hrtf = 0x1992;               // ALC_HRTF_SOFT
    internal const int OutputLimiter = 0x199A;      // ALC_OUTPUT_LIMITER_SOFT

    internal readonly delegate* unmanaged[Cdecl]<byte*, Device*> OpenDevice;
    internal readonly delegate* unmanaged[Cdecl]<Device*, int, int, int, byte> IsRenderFormatSupported;
    internal readonly delegate* unmanaged[Cdecl]<Device*, void*, int, void> RenderSamples;

    private Loopback(ALContext alc)
    {
        OpenDevice = (delegate* unmanaged[Cdecl]<byte*, Device*>)alc.GetProcAddress(null, "alcLoopbackOpenDeviceSOFT");
        IsRenderFormatSupported = (delegate* unmanaged[Cdecl]<Device*, int, int, int, byte>)
            alc.GetProcAddress(null, "alcIsRenderFormatSupportedSOFT");
        RenderSamples = (delegate* unmanaged[Cdecl]<Device*, void*, int, void>)
            alc.GetProcAddress(null, "alcRenderSamplesSOFT");
    }

    /// <summary>Null when the library has no loopback device.</summary>
    internal static Loopback? TryLoad(ALContext alc)
    {
        if (!alc.IsExtensionPresent(null, ExtensionName))
            return null;

        Loopback loopback = new(alc);
        bool complete = loopback.OpenDevice != null
            && loopback.IsRenderFormatSupported != null
            && loopback.RenderSamples != null;
        return complete ? loopback : null;
    }
}
