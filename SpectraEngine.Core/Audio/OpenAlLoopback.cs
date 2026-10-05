using System;
using Silk.NET.OpenAL;

namespace SpectraEngine.Core.Audio;

// OpenAL Soft's loopback device: the mix is rendered into memory on request
// and reaches no sound card. Mono float at 48 kHz.
internal sealed unsafe class OpenAlLoopback
{
    public const int SampleRate = 48000;

    // From include/AL/alext.h, and alc.h for ALC_FREQUENCY.
    private const string Extension = "ALC_SOFT_loopback";
    private const int Frequency = 0x1007;
    private const int FormatChannels = 0x1990;
    private const int FormatType = 0x1991;
    private const int Float = 0x1406;
    private const int Mono = 0x1500;
    private const int Hrtf = 0x1992;
    private const int OutputLimiter = 0x199A;

    private readonly delegate* unmanaged[Cdecl]<byte*, Device*> _openDevice;
    private readonly delegate* unmanaged[Cdecl]<Device*, void*, int, void> _renderSamples;

    private OpenAlLoopback(
        delegate* unmanaged[Cdecl]<byte*, Device*> openDevice,
        delegate* unmanaged[Cdecl]<Device*, void*, int, void> renderSamples)
    {
        _openDevice = openDevice;
        _renderSamples = renderSamples;
    }

    // Null when the library has no loopback device. No device has to be open.
    public static OpenAlLoopback? TryLoad(ALContext alc)
    {
        if (!alc.IsExtensionPresent(null, Extension))
            return null;

        var openDevice = (delegate* unmanaged[Cdecl]<byte*, Device*>)alc.GetProcAddress(null, "alcLoopbackOpenDeviceSOFT");
        var renderSamples = (delegate* unmanaged[Cdecl]<Device*, void*, int, void>)
            alc.GetProcAddress(null, "alcRenderSamplesSOFT");

        return openDevice is null || renderSamples is null ? null : new OpenAlLoopback(openDevice, renderSamples);
    }

    public Device* OpenDevice() => _openDevice(null);

    public Context* CreateContext(ALContext alc, Device* device)
    {
        // A loopback context must be told its format. With the limiter and
        // HRTF off the output is the mix and nothing else.
        int* attributes = stackalloc int[]
        {
            FormatChannels, Mono,
            FormatType, Float,
            Frequency, SampleRate,
            Hrtf, 0,
            OutputLimiter, 0,
            0,
        };

        return alc.CreateContext(device, attributes);
    }

    public void Render(Device* device, Span<float> samples)
    {
        fixed (float* data = samples)
            _renderSamples(device, data, samples.Length);
    }
}
