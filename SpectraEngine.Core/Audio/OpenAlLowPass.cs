using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Silk.NET.OpenAL;

namespace SpectraEngine.Core.Audio;

// The low-pass filter of OpenAL's EFX extension, which the binding has no
// typed calls for. One filter object serves every source: attaching it copies
// its values into the source.
internal sealed unsafe class OpenAlLowPass
{
    // From include/AL/efx.h.
    private const string Extension = "ALC_EXT_EFX";
    private const int DirectFilter = 0x20005;
    private const int FilterType = 0x8001;
    private const int FilterNull = 0x0000;
    private const int FilterLowPass = 0x0001;
    private const int LowPassGainHf = 0x0002;

    private static readonly string[] EntryPoints = ["alGenFilters", "alDeleteFilters", "alFilteri", "alFilterf"];

    private readonly AL _al;
    private readonly delegate* unmanaged[Cdecl]<int, uint*, void> _deleteFilters;
    private readonly delegate* unmanaged[Cdecl]<uint, int, float, void> _filterf;

    // What each source was last given. OpenAL cannot be asked.
    private readonly Dictionary<uint, float> _held = [];

    private uint _filter;

    private OpenAlLowPass(
        AL al,
        delegate* unmanaged[Cdecl]<int, uint*, void> deleteFilters,
        delegate* unmanaged[Cdecl]<uint, int, float, void> filterf,
        uint filter)
    {
        _al = al;
        _deleteFilters = deleteFilters;
        _filterf = filterf;
        _filter = filter;
    }

    // Null when the library has no filters, and the reason says what it lacks.
    // A context must be current.
    // withheld names what to treat as absent, for tests: no library without
    // filters can be loaded beside the packaged one.
    public static OpenAlLowPass? TryCreate(
        AL al, ALContext alc, Device* device, Predicate<string>? withheld, out string reason)
    {
        if (IsWithheld(withheld, Extension) || !alc.IsExtensionPresent(device, Extension))
        {
            reason = $"the device has no {Extension}";
            return null;
        }

        foreach (string name in EntryPoints)
        {
            if (!IsWithheld(withheld, name) && al.GetProcAddress(name) != 0)
                continue;

            reason = $"the library has no {name}";
            return null;
        }

        var genFilters = (delegate* unmanaged[Cdecl]<int, uint*, void>)al.GetProcAddress("alGenFilters");
        var deleteFilters = (delegate* unmanaged[Cdecl]<int, uint*, void>)al.GetProcAddress("alDeleteFilters");
        var filteri = (delegate* unmanaged[Cdecl]<uint, int, int, void>)al.GetProcAddress("alFilteri");
        var filterf = (delegate* unmanaged[Cdecl]<uint, int, float, void>)al.GetProcAddress("alFilterf");

        // Clears what an earlier call left, so the error read below is ours.
        al.GetError();

        uint filter = 0;
        genFilters(1, &filter);

        // Setting the type puts both gains back to 1, so it is set once.
        // AL_LOWPASS_GAIN stays there: loudness goes through the source's gain.
        filteri(filter, FilterType, FilterLowPass);

        AudioError error = al.GetError();
        if (error == AudioError.NoError && filter != 0)
        {
            reason = string.Empty;
            return new OpenAlLowPass(al, deleteFilters, filterf, filter);
        }

        if (filter != 0) deleteFilters(1, &filter);
        reason = $"making the filter reported {error}";
        return null;
    }

    // OpenAL refuses a value outside 0 to 1 and keeps the old one.
    internal static float Clamp(float gainHf) => float.IsNaN(gainHf) ? 1f : Math.Clamp(gainHf, 0f, 1f);

    // A new source has no filter.
    public void Track(uint source) => _held[source] = 1f;

    public void Forget(uint source) => _held.Remove(source);

    // Leaves OpenAL alone when the source has the value already. A source
    // keeps its filter when it is handed to another sound, so this is also
    // what clears it.
    public void Apply(uint source, float gainHf)
    {
        float wanted = Clamp(gainHf);

        ref float held = ref CollectionsMarshal.GetValueRefOrAddDefault(_held, source, out bool isKnown);
        if (isKnown && held == wanted)
            return;

        held = wanted;

        if (wanted >= 1f)
        {
            _al.SetSourceProperty(source, (SourceInteger)DirectFilter, FilterNull);
            return;
        }

        // The attach copies the value. Setting it on the filter alone changes
        // no source.
        _filterf(_filter, LowPassGainHf, wanted);
        _al.SetSourceProperty(source, (SourceInteger)DirectFilter, (int)_filter);
    }

    // Sources keep what they were given. The context must still be current.
    public void Delete()
    {
        if (_filter == 0) return;

        uint filter = _filter;
        _deleteFilters(1, &filter);
        _filter = 0;
        _held.Clear();
    }

    private static bool IsWithheld(Predicate<string>? withheld, string name) => withheld is not null && withheld(name);
}
