using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Audio.Captions;

// Follows each sound the presenter plays through its captions and tells the
// feed which lines are due. A sound is followed while it can be heard, by the
// presenter's own loudness, so a sound and its caption cannot disagree.
// Where a sound is in its playback is the simulation's count, the one a
// voice is started by. Render thread only.
internal sealed class CaptionTracker
{
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
        if (presented.Loudness <= SoundPresenter.SilenceGain)
        {
            presented.Captions.WasHeard = false;
            return;
        }

        if (presented.Captions.Lookup != _feed.Lookup)
            LookUp(ref presented, voices);

        if (presented.Captions.Captions is not { } captions)
            return;

        // A stereo sound plays at the listener.
        Vector3? place = presented.Sound is { IsStereo: true } ? null : presented.Position;

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

        // Whether the sound has a place is in its file.
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

        double now = emitter.PositionAt(tick).Frame / (double)emitter.SampleRate;
        ref CaptionProgress progress = ref presented.Captions;
        IReadOnlyList<CaptionLine> lines = captions.Lines;

        for (int i = 0; i < lines.Count; i++)
        {
            CaptionLine line = lines[i];
            bool isSaid = line.Start <= now && now < line.End;

            // A long frame can pass a short line whole. It still shows.
            if (progress.WasHeard ? HasReached(line.Start, progress.Seconds, now) : isSaid)
                _feed.Show(captions, i, place, presented.Loudness);
            else if (isSaid)
                _feed.Hold(captions, i, place, presented.Loudness);
            else
                _feed.Hear(captions, i, place, presented.Loudness);
        }

        progress.Seconds = now;
    }

    // Whether playback passed a moment since the last look. A looped sound
    // that has turned round has passed everything up to where it is now.
    private static bool HasReached(double moment, double before, double now) =>
        now >= before ? moment > before && moment <= now : moment <= now;
}
