using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.IO;

namespace SpectraEngine.Editor.Sounds;

// One cooked sound in the editor's cache, opened: the cook's stamp, and the
// cooked bytes still on disk behind it. A sound the cook refused has an entry
// too, with no bytes, so it is not cooked again either.
internal sealed class SoundCacheEntry : IDisposable
{
    private readonly Stream _stream;

    private SoundCacheEntry(Stream stream, LooseSoundStamp stamp)
    {
        _stream = stream;
        Stamp = stamp;
    }

    public LooseSoundStamp Stamp { get; }

    public static void Write(Stream stream, LooseSoundResult result)
    {
        LooseSoundStamp.Of(result).Write(stream);
        if (result.Cooked is { } cooked) stream.Write(cooked);
    }

    // Takes the stream over when it returns. Throws on a file that is not an
    // entry this build wrote, and the caller keeps the stream.
    public static SoundCacheEntry Read(Stream stream) => new(stream, LooseSoundStamp.Read(stream));

    // The cooked sound, or null when the bytes on disk are not the ones that
    // were written. Call once: it reads from where the stamp ended.
    public ContentBlob? ReadSound()
    {
        long length = Stamp.CookedLength;
        if (!Stamp.HasSound || length > int.MaxValue || _stream.Length - _stream.Position != length) return null;

        ContentBlob blob = ContentBlob.Rent((int)length, out Span<byte> destination);
        bool intact = false;

        try
        {
            _stream.ReadExactly(destination);
            intact = Stamp.IsStampOf(destination);
        }
        catch (IOException)
        {
            // Unreadable is the same as damaged: the sound is cooked again.
        }

        if (intact) return blob;

        blob.Dispose();
        return null;
    }

    public void Dispose() => _stream.Dispose();
}
