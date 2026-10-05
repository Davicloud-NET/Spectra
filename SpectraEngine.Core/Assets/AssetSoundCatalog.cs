using SpectraEngine.Core.Audio;
using System;
using System.IO;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// Describes sounds from the cooked files the asset manager opens. Asking
/// about a sound opens it, so a level that asks at its start has it loaded
/// before anything plays. Any thread.
/// </summary>
public sealed class AssetSoundCatalog : ISoundCatalog
{
    private const string MissingReason = "no such file is in the project's content";

    private readonly AssetManager _assets;

    /// <summary>A catalog over the sounds <paramref name="assets"/> can open.</summary>
    public AssetSoundCatalog(AssetManager assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets;
    }

    /// <inheritdoc/>
    public bool TryDescribe(string path, out SoundDescription sound, out string reason)
    {
        sound = default;

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "no sound is set";
            return false;
        }

        try
        {
            // Asked first: to the load, a file that is not there was not cooked.
            if (!_assets.AudioExists(path))
            {
                reason = MissingReason;
                return false;
            }

            AudioAsset asset = _assets.LoadAudio(path);
            sound = new SoundDescription(asset.FrameCount, asset.Format.SampleRate, asset.Loop, asset.Markers);
            reason = "";
            return true;
        }
        catch (FileNotFoundException)
        {
            reason = MissingReason;
        }
        catch (ArgumentException)
        {
            reason = "that is not a path inside the project's content";
        }
        // A boundary: whatever else the load throws becomes the reason, so
        // nothing reaches the tick. An uncooked sound says to run the cook.
        catch (Exception failure)
        {
            reason = failure.Message.TrimEnd('.');
        }

        return false;
    }
}
