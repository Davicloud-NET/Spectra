using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio;
using System;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// A cooked sound the asset manager has open: the parsed <c>.saudio</c> header
/// and a live view of its PCM. Owned by the asset manager: release it with
/// <c>AssetManager.UnloadAudio</c>, never by disposing it yourself.
/// </summary>
public sealed class AudioAsset : IDisposable
{
    // Held for the asset's life. Samples is a span into it, and on a mounted
    // pack that is a mapped view: unmapping under a live span is an access violation.
    private ContentBlob? _blob;

    internal AudioAsset(string sourcePath, string resolvedPath, SaudioInfo info, ContentBlob blob)
    {
        SourcePath = sourcePath;
        ResolvedPath = resolvedPath;
        Info = info;
        _blob = blob;
    }

    /// <summary>The content path the caller asked for, e.g. <c>Sounds/door.wav</c>.</summary>
    public string SourcePath { get; }

    /// <summary>The path the bytes actually came from, e.g. <c>Sounds/door.saudio</c>.</summary>
    public string ResolvedPath { get; }

    /// <summary>What the file's header declared.</summary>
    public SaudioInfo Info { get; }

    /// <summary>Rate and channel count.</summary>
    public AudioFormat Format => Info.Format;

    /// <summary>The region the sound repeats, or <see cref="LoopRegion.None"/>.</summary>
    public LoopRegion Loop => Info.Loop;

    /// <summary>Length in sample frames.</summary>
    public long FrameCount => Info.FrameCount;

    /// <summary>True once the manager has released it; the samples are empty afterwards.</summary>
    public bool IsReleased => _blob is null;

    /// <summary>
    /// The interleaved PCM16, read in place from the content source's bytes.
    /// Empty once released.
    /// </summary>
    public ReadOnlySpan<short> Samples =>
        _blob is { } blob ? Info.Pcm(blob.Span) : default;

    /// <summary>Releases the content reference. For the manager to call; idempotent.</summary>
    public void Dispose()
    {
        _blob?.Dispose();
        _blob = null;
    }
}
