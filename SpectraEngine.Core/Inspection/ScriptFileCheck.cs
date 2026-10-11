using SpectraEngine.Core.Assets.Sources;
using System;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// Whether a script's file is there, with the answer kept for a while. The
/// rows are built on every publish, and the question goes to the disk.
/// </summary>
// One answer, for the last path asked: only a selection of one script asks.
public sealed class ScriptFileCheck
{
    private readonly TimeSpan _keepFor;
    private readonly TimeProvider _clock;

    // Weak: the check outlives a level and must not keep its packs open.
    private readonly WeakReference<IContentSource?> _content = new(null);
    private string? _path;
    private bool _exists;
    private long _askedAt;

    /// <summary>Creates a check that keeps an answer for <paramref name="keepFor"/>.</summary>
    /// <param name="clock">What time it is. The system's clock when null.</param>
    public ScriptFileCheck(TimeSpan keepFor, TimeProvider? clock = null)
    {
        _keepFor = keepFor;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>A check that keeps nothing and asks every time.</summary>
    public static ScriptFileCheck EveryTime { get; } = new(TimeSpan.Zero);

    /// <summary>Whether <paramref name="content"/> has the file at <paramref name="path"/>.</summary>
    public bool Exists(IContentSource content, string path)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(path);

        // Shared by every caller that keeps nothing, so it writes nothing.
        if (_keepFor <= TimeSpan.Zero)
            return content.Exists(path);

        long now = _clock.GetTimestamp();
        bool isKept = _content.TryGetTarget(out IContentSource? kept)
            && ReferenceEquals(kept, content)
            && string.Equals(path, _path, StringComparison.Ordinal);

        if (!isKept || _clock.GetElapsedTime(_askedAt, now) >= _keepFor)
        {
            _content.SetTarget(content);
            _path = path;
            _askedAt = now;
            _exists = content.Exists(path);
        }

        return _exists;
    }
}
