using System;
using System.IO;
using System.Threading;

namespace Spectra.Kitchen.Packs;

internal static class AtomicOutput
{
    internal static void Write(string path, Action<Stream> write, CancellationToken cancellationToken = default)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = File.Create(temporary)) { write(stream); stream.Flush(flushToDisk: true); }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
