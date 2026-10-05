using System;
using Silk.NET.OpenAL;

namespace OpenAlFilterSpike.Checks;

// Question 1: the filter entry points through function pointers.
internal static unsafe class EfxChecks
{
    private static readonly string[] EntryPoints =
    [
        "alGenFilters", "alDeleteFilters", "alIsFilter", "alFilteri", "alFilterf", "alGetFilterf", "alSourcei",
    ];

    private static readonly (string Name, int Value)[] Constants =
    [
        ("AL_DIRECT_FILTER", Efx.DirectFilter),
        ("AL_FILTER_TYPE", Efx.FilterType),
        ("AL_FILTER_NULL", Efx.FilterNull),
        ("AL_FILTER_LOWPASS", Efx.FilterLowpass),
        ("AL_LOWPASS_GAIN", Efx.LowpassGain),
        ("AL_LOWPASS_GAINHF", Efx.LowpassGainHf),
    ];

    internal static void Run(Report report, OpenAl lib)
    {
        using Rig rig = Rig.Open(lib);
        AL al = rig.Al;
        Efx efx = rig.Efx;

        report.Info("OpenAL version", al.GetStateProperty(StateString.Version));
        report.Info("OpenAL renderer, vendor", $"{al.GetStateProperty(StateString.Renderer)}, {al.GetStateProperty(StateString.Vendor)}");

        int major = 0;
        int minor = 0;
        lib.Alc.GetContextProperty(rig.Device, (GetContextInteger)Efx.EfxMajorVersion, 1, &major);
        lib.Alc.GetContextProperty(rig.Device, (GetContextInteger)Efx.EfxMinorVersion, 1, &minor);
        report.Check("1a", "ALC_EXT_EFX on the device", lib.Alc.IsExtensionPresent(rig.Device, Efx.ExtensionName), $"EFX {major}.{minor}");

        int found = 0;
        int sameAsExport = 0;
        foreach (string name in EntryPoints)
        {
            nint address = (nint)al.GetProcAddress(name);
            if (address != 0) found++;
            if (al.Context.TryGetProcAddress(name, out nint export) && export == address) sameAsExport++;
        }

        report.Check("1b", "alGetProcAddress", found == EntryPoints.Length,
            $"{found} of {EntryPoints.Length} not null, {sameAsExport} equal to the library's export of that name");

        int matching = 0;
        foreach ((string name, int value) in Constants)
            if (al.GetEnumValue(name) == value) matching++;
        report.Check("1c", "header constants against alGetEnumValue", matching == Constants.Length, $"{matching} of {Constants.Length} equal");

        uint filter = 0;
        efx.GenFilters(1, &filter);
        AudioError error = al.GetError();
        report.Check("1d", "alGenFilters", filter != 0 && error == AudioError.NoError && efx.IsFilter(filter) != 0,
            $"filter {filter}, alIsFilter {efx.IsFilter(filter)}, error {error}");

        // A new filter is AL_FILTER_NULL and has no gains to set.
        efx.Filterf(filter, Efx.LowpassGainHf, 0.25f);
        AudioError beforeType = al.GetError();

        efx.Filteri(filter, Efx.FilterType, Efx.FilterLowpass);
        efx.Filterf(filter, Efx.LowpassGainHf, 0.25f);
        error = al.GetError();
        float readBack = 0;
        efx.GetFilterf(filter, Efx.LowpassGainHf, &readBack);
        report.Check("1e", "alFilteri, alFilterf", error == AudioError.NoError && readBack == 0.25f,
            $"read back {readBack}, error {error}. Before the type was set: {beforeType}");

        efx.Filterf(filter, Efx.LowpassGainHf, 1.5f);
        AudioError tooHigh = al.GetError();
        efx.Filterf(filter, Efx.LowpassGainHf, -0.1f);
        AudioError tooLow = al.GetError();
        efx.Filterf(filter, Efx.LowpassGainHf, 0f);
        AudioError zero = al.GetError();
        efx.Filterf(filter, Efx.LowpassGainHf, float.NaN);
        AudioError nan = al.GetError();
        efx.GetFilterf(filter, Efx.LowpassGainHf, &readBack);
        report.Note("1e", "values outside 0 to 1", $"1.5 gives {tooHigh}, -0.1 gives {tooLow}, 0 gives {zero}, NaN gives {nan}, value is then {readBack}");

        efx.Filterf(filter, Efx.LowpassGainHf, 0.25f);
        efx.Filteri(filter, Efx.FilterType, Efx.FilterLowpass);
        efx.GetFilterf(filter, Efx.LowpassGainHf, &readBack);
        report.Note("1e", "setting the type again", $"gain HF 0.25 became {readBack}");

        uint buffer = rig.CreateBuffer(0.1, new Tone(1000, 0.5));
        uint source = rig.CreateSource(buffer);
        al.GetError();
        efx.Sourcei(source, Efx.DirectFilter, (int)filter);
        error = al.GetError();
        report.Check("1f", "alSourcei(AL_DIRECT_FILTER) by pointer", error == AudioError.NoError, $"error {error}");

        al.SetSourceProperty(source, (SourceInteger)Efx.DirectFilter, (int)filter);
        error = al.GetError();
        report.Check("1g", "the same through Silk.NET, cast enum", error == AudioError.NoError, $"error {error}");

        efx.Sourcei(source, Efx.DirectFilter, 12345);
        AudioError badFilter = al.GetError();
        al.GetSourceProperty(source, (GetSourceInteger)Efx.DirectFilter, out int attached);
        AudioError readFilter = al.GetError();
        report.Note("1g", "bad filter id, and reading it back", $"attaching filter 12345 gives {badFilter}. alGetSourcei gives {attached}, error {readFilter}");

        efx.DeleteFilters(1, &filter);
        error = al.GetError();
        report.Check("1h", "alDeleteFilters", error == AudioError.NoError && efx.IsFilter(filter) == 0,
            $"alIsFilter {efx.IsFilter(filter)}, error {error}");

        al.DeleteSource(source);
        al.DeleteBuffer(buffer);
    }

    // Run with no context current, after every rig is closed.
    internal static void RunWithoutContext(Report report, OpenAl lib)
    {
        nint address = (nint)lib.Al.GetProcAddress("alGenFilters");
        report.Note("1i", "alGetProcAddress, no context current", address != 0 ? "not null" : "null");
    }
}
