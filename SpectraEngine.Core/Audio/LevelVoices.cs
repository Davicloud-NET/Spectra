using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Audio;

// The voices a level's sounds hold on the audio device, and who gets the
// next one: the loudest, with a margin so two equal sounds do not swap.
// Render thread only.
internal sealed class LevelVoices
{
    private readonly record struct Tail(AudioVoice Voice, float SecondsLeft);

    private readonly AudioManager _audio;
    private readonly LevelSoundBank _bank;

    // Voices of sounds the simulation has finished and the device has not.
    private readonly List<Tail> _tails = [];

    public LevelVoices(AudioManager audio, AssetManager assets, ILogger logger)
    {
        _audio = audio;
        _bank = new LevelSoundBank(audio, assets, logger);
    }

    // Sounds that hold a source now.
    public int Count { get; private set; }

    // Sounds the device refused a source, since the last Clear.
    public int RefusedStarts { get; private set; }

    // Takes the source from every sound too quiet to hear, then gives the
    // free ones to the loudest sounds that have none.
    public void HandOut(EntityWorld world, Span<PresentedEmitter> presented)
    {
        for (int i = 0; i < presented.Length; i++)
        {
            if (presented[i].Loudness <= SoundPresenter.SilenceGain)
                Release(ref presented[i]);
        }

        // A source other code plays on, or a sound is playing out on, is not
        // ours to hand out. Asking for more would have the pool cut one off.
        int sources = _audio.SourceCount - (_audio.ActiveVoiceCount - Count);

        for (int next = LoudestWithoutSource(presented); next >= 0; next = LoudestWithoutSource(presented))
        {
            ref PresentedEmitter waiting = ref presented[next];

            // Asked every time: the sound may have been unloaded since.
            if (!_bank.TryGet(in waiting.Emitter, out LevelSound? sound))
            {
                waiting.IsUnplayable = true;
                waiting.Loudness = 0f;
                continue;
            }

            waiting.Sound = sound;

            if (Count >= sources)
            {
                int holder = QuietestWithSource(presented);
                if (holder < 0 || waiting.Loudness <= presented[holder].Loudness * SoundPresenter.ChallengeMargin)
                    break;

                Release(ref presented[holder]);
            }

            if (!TryStart(world, ref waiting, sound))
                break;
        }
    }

    // Lets go of a voice that ended without being told to: the device played
    // it out, or another sound took its source. False when it is still going.
    public bool TryDropEnded(ref PresentedEmitter presented)
    {
        if (presented.Voice is not { IsFinished: true })
            return false;

        presented.Voice = null;
        Count--;
        return true;
    }

    public void Release(ref PresentedEmitter presented)
    {
        if (presented.Voice is not { } voice)
            return;

        _audio.Release(voice);
        presented.Voice = null;
        Count--;
    }

    // For a sound the simulation has finished. The device started it up to
    // a frame after its tick, so it has that much left to play.
    public void LetPlayOut(ref PresentedEmitter presented)
    {
        if (presented.Voice is not { } voice)
            return;

        _tails.Add(new Tail(voice, SoundPresenter.TailSeconds));
        presented.Voice = null;
        Count--;
    }

    // Forgets the voices that have played out, and cuts one that has had its time.
    public void EndTails(float deltaSeconds)
    {
        for (int i = _tails.Count - 1; i >= 0; i--)
        {
            Tail tail = _tails[i];
            float left = tail.SecondsLeft - deltaSeconds;

            if (!tail.Voice.IsFinished && left > 0f)
            {
                _tails[i] = tail with { SecondsLeft = left };
                continue;
            }

            _audio.Release(tail.Voice);
            _tails.RemoveAt(i);
        }
    }

    // For the end of a level, once every sound has let go of its voice.
    // Frees the buffers made for its sounds.
    public void Clear()
    {
        for (int i = 0; i < _tails.Count; i++)
            _audio.Release(_tails[i].Voice);

        _tails.Clear();
        _bank.Clear();
        RefusedStarts = 0;
    }

    public static AudioSourceSettings SettingsFor(in PresentedEmitter presented)
    {
        // OpenAL places only mono sounds, so a stereo one is played at the listener.
        bool atListener = presented.Sound is { IsStereo: true };

        return new AudioSourceSettings(
            presented.Loudness,
            presented.Emitter.Pitch,
            atListener ? Vector3.Zero : presented.Position,
            Vector3.Zero,
            Relative: atListener,
            presented.Smoother.GainHf);
    }

    // False when the device had no source to give.
    private bool TryStart(EntityWorld world, ref PresentedEmitter presented, LevelSound sound)
    {
        AudioVoice? voice = _bank.Start(
            sound, in presented.Emitter, StartFrame(world, in presented.Emitter), SettingsFor(in presented));

        if (voice is null)
        {
            if (!presented.WasRefused)
                RefusedStarts++;

            presented.WasRefused = true;
            return false;
        }

        presented.WasRefused = false;

        // Over before it began: the samples are gone.
        if (voice.IsFinished)
        {
            presented.IsUnplayable = true;
            presented.Loudness = 0f;
            return true;
        }

        presented.Voice = voice;
        Count++;
        return true;
    }

    private static long StartFrame(EntityWorld world, in SoundEmitter emitter)
    {
        long tick = world.TickNumber;
        bool isFresh = (tick - emitter.StartTick) * world.FixedDeltaTime <= SoundPresenter.FreshStartSeconds;

        return isFresh ? 0 : emitter.PositionAt(tick).Frame;
    }

    private static int LoudestWithoutSource(ReadOnlySpan<PresentedEmitter> presented)
    {
        int loudest = -1;
        float loudness = SoundPresenter.SilenceGain;

        for (int i = 0; i < presented.Length; i++)
        {
            if (presented[i].Voice is not null || presented[i].Loudness <= loudness)
                continue;

            loudness = presented[i].Loudness;
            loudest = i;
        }

        return loudest;
    }

    private static int QuietestWithSource(ReadOnlySpan<PresentedEmitter> presented)
    {
        int quietest = -1;
        float loudness = float.PositiveInfinity;

        for (int i = 0; i < presented.Length; i++)
        {
            if (presented[i].Voice is null || presented[i].Loudness >= loudness)
                continue;

            loudness = presented[i].Loudness;
            quietest = i;
        }

        return quietest;
    }
}
