using System;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// One playing sound, handed out by <see cref="AudioManager"/>. Every method
/// is a no-op once <see cref="IsFinished"/> is true, because the source may
/// already carry a different sound.
/// </summary>
public abstract class AudioVoice
{
    private protected readonly IAudioBackend Backend;

    private protected AudioVoice(IAudioBackend backend, uint source, AudioSourceSettings settings)
    {
        Backend = backend;
        Source = source;
        Settings = settings;
    }

    internal uint Source { get; private set; }

    /// <summary>Gain, pitch, placement.</summary>
    public AudioSourceSettings Settings { get; private set; }

    /// <summary>True once the sound is over.</summary>
    public bool IsFinished { get; private protected set; }

    /// <summary>Moves or re-levels a sound that is still playing.</summary>
    public void Configure(in AudioSourceSettings settings)
    {
        if (IsFinished) return;
        Settings = settings;
        Backend.ConfigureSource(Source, in settings);
    }

    /// <summary>
    /// Ends the sound now. The source returns to the pool on the next
    /// <see cref="AudioManager.Update"/>.
    /// </summary>
    public void Stop()
    {
        if (IsFinished) return;
        Backend.Stop(Source);
        IsFinished = true;
    }

    // False once the source can be reclaimed.
    internal abstract bool Update();

    internal virtual void Detach()
    {
        IsFinished = true;
        Source = 0;
    }
}

/// <summary>
/// A whole clip bound to a source and played once. Looping clips use
/// <see cref="StreamingVoice"/> instead.
/// </summary>
public sealed class StaticVoice : AudioVoice
{
    internal StaticVoice(IAudioBackend backend, uint source, AudioClip clip, AudioSourceSettings settings)
        : base(backend, source, settings)
    {
        Clip = clip;
        backend.ConfigureSource(source, in settings);
        backend.SetSourceBuffer(source, clip.Buffer);
        backend.Play(source);
    }

    // So DestroyClip can find the voices holding its buffer.
    internal AudioClip Clip { get; }

    internal override bool Update()
    {
        if (IsFinished) return false;

        // Paused is live: reclaiming would lose the resume offset.
        AudioSourceState state = Backend.GetSourceState(Source);
        if (state is AudioSourceState.Playing or AudioSourceState.Paused)
            return true;

        IsFinished = true;
        return false;
    }
}
