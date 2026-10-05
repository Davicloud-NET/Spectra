using System;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// One source kept apart from the pool, for a sound that plays outside a
/// level. It carries one sound at a time. Render thread only.
/// </summary>
// Not a pool source, so the pool never hands it to a level sound and it
// never takes one from a level. The driver is asked for it the first time
// something plays on it, so a game that previews nothing has no extra source.
public sealed class AudioPreviewSource
{
    private IAudioBackend? _backend;
    private StreamingVoice? _voice;
    private uint _source;

    /// <summary>Whether a sound is playing on it.</summary>
    public bool IsPlaying => _voice is not null;

    /// <summary>
    /// Plays a sound from its start, in place of what was playing. False when
    /// audio is off, the driver has no source left to give, or the sound is empty.
    /// </summary>
    public bool Play(IAudioSampleProvider provider, in AudioSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(provider);

        Stop();

        if (_backend is not { } backend)
            return false;

        if (_source == 0 && !backend.TryCreateSource(out _source))
        {
            _source = 0;
            return false;
        }

        var voice = new StreamingVoice(backend, _source, provider, settings);
        if (voice.IsFinished)
        {
            voice.Detach();
            return false;
        }

        _voice = voice;
        return true;
    }

    /// <summary>Stops what is playing. The source is kept for the next sound.</summary>
    public void Stop()
    {
        if (_voice is not { } voice)
            return;

        voice.Stop();
        voice.Detach();
        _voice = null;
    }

    internal void Attach(IAudioBackend backend) => _backend = backend;

    // Refills the queue, and lets go of a sound that has played to its end.
    internal void Update()
    {
        if (_voice is not { } voice || voice.Update())
            return;

        voice.Detach();
        _voice = null;

        // Rewound, as the pool leaves a source a sound has given back.
        _backend?.Stop(_source);
    }

    // Before the device closes.
    internal void Release()
    {
        Stop();

        if (_source != 0)
            _backend?.DestroySource(_source);

        _source = 0;
        _backend = null;
    }
}
