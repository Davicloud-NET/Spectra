using SpectraEngine.Core.Assets;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace SpectraEngine.Core.Audio;

// One sound file being read for the preview on a pool thread. An editor cooks
// a file the first time it is read, which is too long to hold a frame for.
internal sealed class SoundPreviewLoad
{
    private const int Reading = 0;
    private const int Landed = 1;
    private const int Abandoned = 2;

    private readonly AssetManager _assets;

    // Written by the read before the state says it has landed.
    private AudioAsset? _asset;
    private string _refusal = string.Empty;
    private int _state;

    private SoundPreviewLoad(AssetManager assets, string request)
    {
        _assets = assets;
        Request = request;
    }

    // What was asked for, as the host wrote it.
    public string Request { get; }

    public bool HasLanded => Volatile.Read(ref _state) == Landed;

    public static SoundPreviewLoad Start(AssetManager assets, string request)
    {
        var load = new SoundPreviewLoad(assets, request);
        ThreadPool.QueueUserWorkItem(static load => load.Read(), load, preferLocal: false);
        return load;
    }

    // Opens a copy of the sound that is the caller's alone. False when the
    // file cannot be read, and refusal then says why. Any thread.
    public static bool TryOpen(
        AssetManager assets, string path, [NotNullWhen(true)] out AudioAsset? asset, out string refusal)
    {
        try
        {
            asset = assets.OpenAudio(path);
            refusal = string.Empty;
            return true;
        }
        // A boundary: whatever the read throws becomes the reason, and nothing
        // reaches the frame loop.
        catch (Exception failure)
        {
            asset = null;
            refusal = failure.Message;
            return false;
        }
    }

    // Hands the sound to the caller, who owns it from here. Only for a read
    // that has landed.
    public bool TryTake([NotNullWhen(true)] out AudioAsset? asset, out string refusal)
    {
        asset = _asset;
        _asset = null;
        refusal = _refusal;
        return asset is not null;
    }

    // Nobody wants the sound any more. It is released here if the read has
    // landed, and by the read when it lands otherwise.
    public void Abandon()
    {
        if (Interlocked.Exchange(ref _state, Abandoned) == Landed)
            _asset?.Dispose();
    }

    private void Read()
    {
        TryOpen(_assets, Request, out _asset, out _refusal);

        if (Interlocked.CompareExchange(ref _state, Landed, Reading) != Reading)
            _asset?.Dispose();
    }
}
