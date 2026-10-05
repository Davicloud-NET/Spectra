using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

// A level whose sounds have captions, run a tick and a frame at a time the
// way the engine runs them, with the log view reading the feed.
internal sealed class CaptionFeedRig : IDisposable
{
    // A tenth of a second, mono.
    public const string Click = "Sounds/click.wav";

    // One metre in front of the listener: at full volume.
    public static readonly Vector3 Near = new(0, 0, -1);

    // Past every default sound's maximum distance.
    public static readonly Vector3 OutOfHearing = new(0, 0, -90);

    private readonly CaptionLogView _view;
    private int _placed;

    public CaptionFeedRig(CaptionMode mode = CaptionMode.All)
    {
        Sound = new SoundPresenterRig();
        Sound.Cook(Click, HandBuiltSaudio.Resident(frames: SoundPresenterRig.Rate / 10));
        Sound.Listen(Vector3.Zero);
        Feed.Mode = mode;
        _view = new CaptionLogView(ViewLog);
    }

    public SoundPresenterRig Sound { get; }

    public CaptionFeed Feed => Sound.Captions;

    public IReadOnlyList<Caption> Shown => Feed.Captions;

    // What the log view wrote.
    public CapturingLogger ViewLog { get; } = new();

    public IReadOnlyList<string> Written => ViewLog.MessagesAt(LogLevel.Information);

    public void Dispose() => Sound.Dispose();

    // Writes a language's caption file.
    public void Captions(string language, string text) => Sound.WriteText(CaptionFile.PathFor(language), text);

    // Writes a sound's subtitle file in a language.
    public void Subtitles(string sound, string language, string text) =>
        Sound.WriteText(SubtitlePath.For(sound, language), text);

    public SceneNode Place(Vector3 position) => Sound.Place($"speaker{_placed++}", position);

    // Plays a sound once from a new node near the listener.
    public int Play(string sound) => Play(sound, Near);

    public int Play(string sound, Vector3 position, bool looped = false) =>
        Play(sound, Place(position), looped ? SoundPresenterRig.Looped : SoundPresenterRig.Once);

    public int Play(string sound, SceneNode node, SoundEmitterSettings settings) => Sound.Play(node, sound, settings);

    // A tick of the level and a frame of sound each, sixty to the second.
    public void Step(int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            Sound.Tick();
            Sound.Frame();
            _view.Update(Feed);
        }
    }

    // Steps up to a moment, in seconds since the level started. A sound
    // played before the first step is that far into its playback then.
    public void RunTo(double seconds)
    {
        int target = (int)Math.Round(seconds * 60d);
        Step(target - (int)Sound.World.TickNumber);
    }
}
