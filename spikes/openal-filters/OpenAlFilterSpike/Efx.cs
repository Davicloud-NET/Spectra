using Silk.NET.OpenAL;

namespace OpenAlFilterSpike;

/// <summary>
/// The EFX filter entry points, fetched with alGetProcAddress. A context must
/// be current when this is built.
/// </summary>
internal sealed unsafe class Efx
{
    // Values from OpenAL Soft 1.23.1, include/AL/efx.h.
    internal const string ExtensionName = "ALC_EXT_EFX";
    internal const int EfxMajorVersion = 0x20001;   // ALC_EFX_MAJOR_VERSION
    internal const int EfxMinorVersion = 0x20002;   // ALC_EFX_MINOR_VERSION
    internal const int DirectFilter = 0x20005;      // AL_DIRECT_FILTER
    internal const int FilterType = 0x8001;         // AL_FILTER_TYPE
    internal const int FilterNull = 0x0000;         // AL_FILTER_NULL
    internal const int FilterLowpass = 0x0001;      // AL_FILTER_LOWPASS
    internal const int LowpassGain = 0x0001;        // AL_LOWPASS_GAIN
    internal const int LowpassGainHf = 0x0002;      // AL_LOWPASS_GAINHF

    internal readonly delegate* unmanaged[Cdecl]<int, uint*, void> GenFilters;
    internal readonly delegate* unmanaged[Cdecl]<int, uint*, void> DeleteFilters;
    internal readonly delegate* unmanaged[Cdecl]<uint, byte> IsFilter;
    internal readonly delegate* unmanaged[Cdecl]<uint, int, int, void> Filteri;
    internal readonly delegate* unmanaged[Cdecl]<uint, int, float, void> Filterf;
    internal readonly delegate* unmanaged[Cdecl]<uint, int, float*, void> GetFilterf;

    // Core AL, not EFX. Fetched the same way to show that it can be. The typed
    // Silk.NET call with a cast enum does the same job.
    internal readonly delegate* unmanaged[Cdecl]<uint, int, int, void> Sourcei;

    private Efx(AL al)
    {
        GenFilters = (delegate* unmanaged[Cdecl]<int, uint*, void>)al.GetProcAddress("alGenFilters");
        DeleteFilters = (delegate* unmanaged[Cdecl]<int, uint*, void>)al.GetProcAddress("alDeleteFilters");
        IsFilter = (delegate* unmanaged[Cdecl]<uint, byte>)al.GetProcAddress("alIsFilter");
        Filteri = (delegate* unmanaged[Cdecl]<uint, int, int, void>)al.GetProcAddress("alFilteri");
        Filterf = (delegate* unmanaged[Cdecl]<uint, int, float, void>)al.GetProcAddress("alFilterf");
        GetFilterf = (delegate* unmanaged[Cdecl]<uint, int, float*, void>)al.GetProcAddress("alGetFilterf");
        Sourcei = (delegate* unmanaged[Cdecl]<uint, int, int, void>)al.GetProcAddress("alSourcei");
    }

    private bool IsComplete =>
        GenFilters != null && DeleteFilters != null && IsFilter != null && Filteri != null
        && Filterf != null && GetFilterf != null && Sourcei != null;

    /// <summary>Null when the device has no EFX or an entry point is missing.</summary>
    internal static Efx? TryLoad(AL al, ALContext alc, Device* device)
    {
        if (!alc.IsExtensionPresent(device, ExtensionName))
            return null;

        Efx efx = new(al);
        return efx.IsComplete ? efx : null;
    }

    /// <summary>A low-pass filter object with both gains at 1.</summary>
    internal uint CreateLowpass()
    {
        uint filter = 0;
        GenFilters(1, &filter);
        Filteri(filter, FilterType, FilterLowpass);
        return filter;
    }

    internal void Delete(uint filter) => DeleteFilters(1, &filter);
}
