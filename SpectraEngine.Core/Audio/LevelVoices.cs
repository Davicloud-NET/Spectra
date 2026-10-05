using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using System;
using System.Numerics;

namespace SpectraEngine.Core.Audio;

// The voices a level's sounds hold on the audio device, and who gets the
// next one: the loudest, with a margin so two equal sounds do not swap.
// Render thread only.
internal sealed class LevelVoices
{
    private readonly AudioManager _audio;
    private readonly LevelSoundBank _bank;

    public LevelVoices(AudioManager audio, AssetManager assets)
    {
        _audio = audio;
        _bank = new LevelSoundBank(audio, assets);
    }

    // Sounds that hold a source now.
    public int Count { get; private set; }

    // Sounds the device refused a source, since the last Clear.
    public int RefusedStarts { get; private set; }

    // Takes the source from every sound too quiet to hear, then gives the
    // free ones to the loudest sounds that have none.
    public void HandOut(EntityWorld world, ReadOnlySpan<SoundEmitter> playing, Span<PresentedEmitter> presented)
    {
        for (int i = 0; i < presented.Length; i++)
        {
            if (presented[i].Loudness <= SoundPresenter.SilenceGain)
                Release(ref presented[i]);
        }

        // What other code plays on is not ours to hand out. Asking for more
        // would have the pool cut off one of these voices.
        int sources = _audio.SourceCount - (_audio.ActiveVoiceCount - Count);

        for (int next = LoudestWithoutSource(presented); next >= 0; next = LoudestWithoutSource(presented))
        {
            ref PresentedEmitter waiting = ref presented[next];

            LevelSound? sound = waiting.Sound;
            if (sound is null && !_bank.TryGet(in playing[next], out sound))
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

            if (!TryStart(world, ref waiting, in playing[next], sound))
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

    // For the end of a level, once every voice has been released. Frees the
    // buffers made for its sounds.
    public void Clear()
    {
        _bank.Clear();
        RefusedStarts = 0;
    }

    public static AudioSourceSettings SettingsFor(in PresentedEmitter presented, in SoundEmitter emitter)
    {
        // OpenAL places only mono sounds, so a stereo one is played at the listener.
        bool atListener = presented.Sound is { IsStereo: true };

        return new AudioSourceSettings(
            presented.Loudness,
            emitter.Pitch,
            atListener ? Vector3.Zero : presented.Position,
            Vector3.Zero,
            Relative: atListener,
            presented.Smoother.GainHf);
    }

    // False when the device had no source to give.
    private bool TryStart(
        EntityWorld world, ref PresentedEmitter presented, in SoundEmitter emitter, LevelSound sound)
    {
        AudioVoice? voice = _bank.Start(
            sound, in emitter, StartFrame(world, in emitter), SettingsFor(in presented, in emitter));

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
