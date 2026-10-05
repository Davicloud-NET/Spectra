using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Audio.Captions;

// Follows each sound the presenter plays through its captions and tells the
// feed which lines are due. A sound is followed while the presenter's own
// loudness says it is heard, so a sound and its caption cannot disagree.
// Render thread only.
internal sealed class CaptionTracker
{
    // A caption comes up for a sound at least this loud at the listener, and
    // is let go once the sound falls under the lower number. The gap keeps a
    // sound at the edge of hearing from showing over and over. Both are far
    // above the presenter's own silence, which only says a sound needs no
    // source: at a five hundredth of full volume nobody hears it.
    internal const float HeardFrom = 0.02f;
    internal const float HeardDownTo = 0.01f;

    private readonly CaptionFeed _feed;

    public CaptionTracker(CaptionFeed feed) => _feed = feed;

    public void Update(EntityWorld world, Span<PresentedEmitter> presented, LevelVoices voices, float deltaSeconds)
    {
        _feed.BeginFrame(deltaSeconds);

        if (_feed.Mode != CaptionMode.Off)
        {
            long tick = world.TickNumber;
            for (int i = 0; i < presented.Length; i++)
                Follow(ref presented[i], voices, tick);
        }

        _feed.EndFrame();
    }

    public void EndLevel() => _feed.EndLevel();

    private void Follow(ref PresentedEmitter presented, LevelVoices voices, long tick)
    {
        if (presented.Loudness < (presented.Captions.WasHeard ? HeardDownTo : HeardFrom))
        {
            if (presented.HasPlayedOut && presented.VoiceLead > 0f)
                ShowWhatWasSaidUnseen(ref presented, tick);

            presented.Captions.WasHeard = false;
            return;
        }

        if (presented.Captions.Lookup != _feed.Lookup)
            LookUp(ref presented, voices);

        if (presented.Captions.Captions is not { } captions)
            return;

        // A sound that is not placed plays at the listener.
        Vector3? place = presented.IsPlaced ? presented.Position : null;

        if (captions.Kind == CaptionKind.Voice)
            FollowSpeech(captions, ref presented, place, tick);
        else
            FollowSound(captions, in presented, place);

        presented.Captions.WasHeard = true;
    }

    private void LookUp(ref PresentedEmitter presented, LevelVoices voices)
    {
        SoundCaptions? found = _feed.Library.Find(presented.Emitter.Path, _feed.Language);
        bool isShown = found is not null && _feed.Shows(found.Kind);

        presented.Captions = new CaptionProgress { Captions = isShown ? found : null, Lookup = _feed.Lookup };

        // Whether the sound can have a place is in its file.
        if (isShown)
            voices.Load(ref presented);
    }

    // The caption shows when the sound comes into hearing. A sound that
    // plays once keeps it up for as long as it plays. A looped one does not:
    // its caption goes once it has been read.
    private void FollowSound(SoundCaptions captions, in PresentedEmitter presented, Vector3? place)
    {
        if (!presented.Captions.WasHeard)
            _feed.Show(captions, 0, place, presented.Loudness);
        else if (presented.Emitter.Loop.IsLooping)
            _feed.Hear(captions, 0, place, presented.Loudness);
        else
            _feed.Hold(captions, 0, place, presented.Loudness);
    }

    // A line shows when playback reaches its start, never before. A sound
    // that comes into hearing in the middle of a line shows that line.
    private void FollowSpeech(SoundCaptions captions, ref PresentedEmitter presented, Vector3? place, long tick)
    {
        ref readonly SoundEmitter emitter = ref presented.Emitter;
        if (emitter.SampleRate <= 0)
            return;

        ref CaptionProgress progress = ref presented.Captions;
        SoundPosition at = emitter.PositionAt(tick);
        double now = SecondsHeard(in presented, at);
        Passage passed = progress.WasHeard ? Passage.Since(in progress, now, at.Pass, in emitter) : default;
        IReadOnlyList<CaptionLine> lines = captions.Lines;

        for (int i = 0; i < lines.Count; i++)
        {
            CaptionLine line = lines[i];
            bool isSaid = line.Start <= now && now < line.End;

            // A long frame can pass a short line whole. It still shows.
            if (progress.WasHeard ? passed.Holds(line.Start) : isSaid)
                _feed.Show(captions, i, place, presented.Loudness);
            else if (isSaid)
                _feed.Hold(captions, i, place, presented.Loudness);
            else
                _feed.Hear(captions, i, place, presented.Loudness);
        }

        progress.Seconds = now;
        progress.Pass = at.Pass;
    }

    // The voice ended by itself ahead of the ticks: in a frame so long that
    // they are not there yet, or with its pitch bent up. The presenter
    // follows it no further. The lines it said that nobody saw still show,
    // each for its reading time.
    private void ShowWhatWasSaidUnseen(ref PresentedEmitter presented, long tick)
    {
        ref readonly CaptionProgress progress = ref presented.Captions;
        ref readonly SoundEmitter emitter = ref presented.Emitter;

        if (!progress.WasHeard
            || progress.Lookup != _feed.Lookup
            || progress.Captions is not { Kind: CaptionKind.Voice } captions
            || emitter.SampleRate <= 0)
        {
            return;
        }

        // No further than the device can have come. A voice also ends when
        // another sound takes its source, and what it had left was not said.
        double length = emitter.FrameCount / (double)emitter.SampleRate;
        double reached = Math.Min(length, SecondsPlayed(in presented, emitter.PositionAt(tick)));
        Vector3? place = presented.IsPlaced ? presented.Position : null;

        for (int i = 0; i < captions.Lines.Count; i++)
        {
            double start = captions.Lines[i].Start;
            if (start > progress.Seconds && start <= reached && start < length)
                _feed.Show(captions, i, place, 0f);
        }
    }

    // Seconds into the sound that have been heard. A sound that plays once
    // never goes back: it may lose the voice that was ahead of the ticks.
    private static double SecondsHeard(in PresentedEmitter presented, SoundPosition at)
    {
        double seconds = presented.Voice is not null
            ? SecondsPlayed(in presented, at)
            : at.Frame / (double)presented.Emitter.SampleRate;

        bool canGoBack = presented.Emitter.Loop.IsLooping || !presented.Captions.WasHeard;
        return canGoBack ? seconds : Math.Max(seconds, presented.Captions.Seconds);
    }

    // The level's count, and on top what the voice has played ahead of it:
    // through a long frame that held the ticks back, or with its pitch bent
    // up. A voice that is behind counts as level with it. It is cut at the
    // level's end, and a line that waited for it would never show.
    private static double SecondsPlayed(in PresentedEmitter presented, SoundPosition at) =>
        (at.Frame / (double)presented.Emitter.SampleRate)
        + (MathF.Max(presented.VoiceLead, 0f) * presented.Emitter.Pitch);

    // The stretch of a sound that playback went over since the last look.
    // When a loop turned round that is two: the rest of the pass, and from
    // the loop's start to where it is now. What lies before the loop's start
    // is not said again.
    private readonly struct Passage
    {
        private readonly double _after;
        private readonly double _upTo;
        private readonly double _againFrom;
        private readonly double _againUpTo;

        private Passage(double after, double upTo, double againFrom, double againUpTo)
        {
            _after = after;
            _upTo = upTo;
            _againFrom = againFrom;
            _againUpTo = againUpTo;
        }

        public static Passage Since(in CaptionProgress progress, double now, long pass, in SoundEmitter emitter)
        {
            if (pass == progress.Pass)
                return new Passage(progress.Seconds, now, double.PositiveInfinity, double.NegativeInfinity);

            double rate = emitter.SampleRate;
            double loopEnd = emitter.Loop.EndFrame / rate;
            return new Passage(progress.Seconds, Math.BitDecrement(loopEnd), emitter.Loop.StartFrame / rate, now);
        }

        public bool Holds(double moment) =>
            (moment > _after && moment <= _upTo) || (moment >= _againFrom && moment <= _againUpTo);
    }
}
