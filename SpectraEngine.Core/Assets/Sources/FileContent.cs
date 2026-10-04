using System;
using System.IO;
using System.Threading;

namespace SpectraEngine.Core.Assets.Sources;

// The one place content bytes are read off the filesystem.
// Retries because a hot-reload notification can arrive while the tool that
// saved the file still holds a write lock.
internal static class FileContent
{
    private const int ReadRetryDelayMs = 20;
    private const int ReadAttempts = 3;

    // Reads the whole file into a pooled blob the caller disposes. Any thread.
    public static ContentBlob Read(string absolutePath)
    {
        for (int attempt = 0; attempt < ReadAttempts; attempt++)
        {
            try
            {
                // FileShare.ReadWrite: don't add a lock while another tool is writing.
                using var stream = new FileStream(
                    absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                // ReadExactly below: a short read would leave stale pooled bytes in the blob.
                long length = stream.Length;
                if (length > int.MaxValue)
                    throw new IOException($"Content file '{absolutePath}' is larger than 2 GB.");

                ContentBlob blob = ContentBlob.Rent((int)length, out Span<byte> destination);
                try
                {
                    stream.ReadExactly(destination);
                }
                catch
                {
                    blob.Dispose();
                    throw;
                }

                return blob;
            }
            catch (IOException) when (attempt < ReadAttempts - 1)
            {
                Thread.Sleep(ReadRetryDelayMs);
            }
        }

        throw new IOException($"Could not read '{absolutePath}' after {ReadAttempts} attempts.");
    }
}
