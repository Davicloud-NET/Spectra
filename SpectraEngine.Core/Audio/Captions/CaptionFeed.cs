using SpectraEngine.Core.Projects;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// The captions that show right now. The sound presenter fills it once a
/// frame, and whatever draws captions only reads it: the engine's own view, a
/// game that restyles that view, or a game that draws its own. The feed keeps
/// the rules about hearing and reading. Nothing about looks is in it.
/// Render thread only. Another thread reads the list a
/// <see cref="Hosting.FrameSnapshot"/> carries.
/// </summary>
// A caption is one line of one sound. Several playing copies of the sound
// share it, so a sound that plays again refreshes its caption and adds none.
public sealed class CaptionFeed
{
    private Shown[] _shown = new Shown[8];
    private int _count;

    private Caption[] _published = [];
    private bool _isPublishedStale;

    private CaptionMode _mode = CaptionMode.Voice;

    // Null follows the project's language.
    private string? _language;

    // The language the captions on show were looked up in.
    private string _shownLanguage;

    /// <param name="library">Where each sound's captions are looked up.</param>
    public CaptionFeed(CaptionLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        Library = library;
        _shownLanguage = library.ProjectLanguage;
    }

    /// <summary>Where each sound's captions are looked up.</summary>
    public CaptionLibrary Library { get; }

    /// <summary>
    /// Which captions show. Starts at speech only. A caption of a kind that
    /// is switched off goes at once.
    /// </summary>
    public CaptionMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;

            _mode = value;
            Lookup++;
            RemoveHidden();
        }
    }

    /// <summary>
    /// The language captions are looked up in, as a <see cref="LanguageTag"/>.
    /// The project's language until one is set. A sound with nothing in it
    /// gets its caption in the project's language.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a language tag.</exception>
    public string Language
    {
        get => _language ?? Library.ProjectLanguage;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!LanguageTag.IsValid(value))
                throw new ArgumentException($"'{value}' is not a language tag such as en, de or pt-br.", nameof(value));

            _language = value;
        }
    }

    /// <summary>
    /// The feed's clock, in seconds. It runs while a level plays.
    /// <see cref="Caption.StartedAt"/> and <see cref="Caption.EarliestEnd"/>
    /// are times on it.
    /// </summary>
    public double Now { get; private set; }

    /// <summary>
    /// The id of the caption that appeared last, or zero when none has yet.
    /// Ids only go up, so a reader that keeps this number can tell that a
    /// caption has appeared since without reading the list.
    /// </summary>
    public long LastId { get; private set; }

    /// <summary>
    /// The captions that show now, by when each started and then by id. The
    /// same list until something in it changes, and a list is never changed
    /// once it has been handed out.
    /// </summary>
    public IReadOnlyList<Caption> Captions
    {
        get
        {
            if (_isPublishedStale)
            {
                _published = Publish();
                _isPublishedStale = false;
            }

            return _published;
        }
    }

    // Goes up when what a sound's captions are may have changed: another
    // language, another mode, another level.
    internal int Lookup { get; private set; } = 1;

    internal bool Shows(CaptionKind kind) =>
        _mode == CaptionMode.All || (_mode == CaptionMode.Voice && kind == CaptionKind.Voice);

    internal void BeginFrame(float deltaSeconds)
    {
        if (deltaSeconds > 0f)
            Now += deltaSeconds;

        string language = Language;
        if (!string.Equals(language, _shownLanguage, StringComparison.Ordinal))
        {
            _shownLanguage = language;
            Lookup++;
            RemoveAll();
        }

        for (int i = 0; i < _count; i++)
        {
            _shown[i].IsWanted = false;
            _shown[i].IsHeard = false;
        }
    }

    // The line is due: it appears, or a caption that still shows starts its
    // reading time over.
    internal void Show(SoundCaptions source, int line, Vector3? place, float loudness)
    {
        int at = IndexOf(source, line);
        if (at < 0)
            at = Add(source, line);

        ref Shown shown = ref _shown[at];
        double until = Now + shown.ReadingSeconds;
        if (until > shown.EarliestEnd)
        {
            shown.EarliestEnd = until;
            _isPublishedStale = true;
        }

        shown.IsWanted = true;
        Listen(ref shown, place, loudness);
    }

    // The line is still being said. Nothing appears because of this.
    internal void Hold(SoundCaptions source, int line, Vector3? place, float loudness)
    {
        int at = IndexOf(source, line);
        if (at < 0)
            return;

        _shown[at].IsWanted = true;
        Listen(ref _shown[at], place, loudness);
    }

    // The line's sound is heard and the line is not being said. A caption
    // that is still up for reading follows its sound.
    internal void Hear(SoundCaptions source, int line, Vector3? place, float loudness)
    {
        int at = IndexOf(source, line);
        if (at >= 0)
            Listen(ref _shown[at], place, loudness);
    }

    internal void EndFrame()
    {
        int kept = 0;
        for (int i = 0; i < _count; i++)
        {
            ref Shown shown = ref _shown[i];
            if (!shown.IsWanted && Now >= shown.EarliestEnd)
            {
                _isPublishedStale = true;
                continue;
            }

            // A caption that outlasts its sound stays where the sound was.
            float audibility = shown.IsHeard ? shown.Loudness : 0f;
            Vector3? place = shown.IsHeard ? shown.Place : shown.Position;
            if (audibility != shown.Audibility || place != shown.Position)
            {
                shown.Audibility = audibility;
                shown.Position = place;
                _isPublishedStale = true;
            }

            _shown[kept++] = shown;
        }

        Truncate(kept);
    }

    // The level ended. Its captions go with it, and the files are read again
    // for the next one, so an edit made in between shows.
    internal void EndLevel()
    {
        Lookup++;
        RemoveAll();
        Library.Reload();
    }

    private void Listen(ref Shown shown, Vector3? place, float loudness)
    {
        // Of several copies of a sound, the loudest says where it is.
        if (shown.IsHeard && loudness <= shown.Loudness)
            return;

        shown.IsHeard = true;
        shown.Loudness = Math.Clamp(loudness, 0f, 1f);
        shown.Place = place;
    }

    private int IndexOf(SoundCaptions source, int line)
    {
        for (int i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_shown[i].Source, source) && _shown[i].Line == line)
                return i;
        }

        return -1;
    }

    // At the end: a new caption is the latest to start and has the highest
    // id, so the list stays in order.
    private int Add(SoundCaptions source, int line)
    {
        if (_count == _shown.Length)
            Array.Resize(ref _shown, _shown.Length * 2);

        CaptionLine words = source.Lines[line];
        _shown[_count] = new Shown
        {
            Id = ++LastId,
            Source = source,
            Line = line,
            StartedAt = Now,
            ReadingSeconds = CaptionTiming.ReadingSeconds(words.Text),
        };

        _isPublishedStale = true;
        return _count++;
    }

    private void RemoveHidden()
    {
        int kept = 0;
        for (int i = 0; i < _count; i++)
        {
            if (Shows(_shown[i].Source.Kind))
                _shown[kept++] = _shown[i];
        }

        _isPublishedStale |= kept != _count;
        Truncate(kept);
    }

    private void RemoveAll()
    {
        _isPublishedStale |= _count > 0;
        Truncate(0);
    }

    private void Truncate(int count)
    {
        if (count < _count)
            Array.Clear(_shown, count, _count - count);

        _count = count;
    }

    private Caption[] Publish()
    {
        if (_count == 0)
            return [];

        var captions = new Caption[_count];
        for (int i = 0; i < _count; i++)
        {
            ref readonly Shown shown = ref _shown[i];
            CaptionLine words = shown.Source.Lines[shown.Line];
            captions[i] = new Caption(
                shown.Id,
                shown.Source.Kind,
                words.Text,
                words.Speaker,
                shown.Position,
                shown.Audibility,
                shown.StartedAt,
                shown.EarliestEnd);
        }

        return captions;
    }

    // One caption on show.
    private struct Shown
    {
        public long Id;
        public SoundCaptions Source;
        public int Line;
        public double StartedAt;
        public double EarliestEnd;
        public double ReadingSeconds;

        // As the list has them.
        public Vector3? Position;
        public float Audibility;

        // This frame: whether the line is being said, and the loudest copy
        // of its sound that is heard.
        public bool IsWanted;
        public bool IsHeard;
        public float Loudness;
        public Vector3? Place;
    }
}
