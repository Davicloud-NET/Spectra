using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.DXGI;
using System;
using System.Text;
using DxgiApi = Silk.NET.DXGI.DXGI;

namespace SpectraEngine.Core.Graphics;

// Drains DXGI's debug queue into the logger. Swap-chain rejections
// (ResizeBuffers, Present) are explained here and not in the device queues.
// No-op unless the factory was created with the debug flag and Graphics Tools
// is installed.
internal sealed unsafe class DxgiDebugMessages : IDisposable
{
    // DXGI_CREATE_FACTORY_DEBUG
    internal const uint CreateFactoryDebug = 0x1;

    // DXGI_DEBUG_ALL
    private static readonly Guid DebugAll =
        new(0xe48ae283, 0xda80, 0x490b, 0x87, 0xe6, 0x43, 0xe9, 0xa9, 0xcf, 0xda, 0x08);

    private ComPtr<IDXGIInfoQueue> _queue;

    internal bool IsAvailable => _queue.Handle is not null;

    // Always returns an instance; one without a queue no-ops.
    internal static DxgiDebugMessages Acquire(DxgiApi dxgi)
    {
        var messages = new DxgiDebugMessages();

        IDXGIInfoQueue* queue = null;
        Guid guid = IDXGIInfoQueue.Guid;
        if (dxgi.GetDebugInterface1(0u, &guid, (void**)&queue) >= 0 && queue is not null)
            messages._queue = ComOwnership.Own(queue);

        return messages;
    }

    // Render thread, once per frame. Returns the error and corruption count.
    internal int Drain(ILogger logger, string backend)
    {
        if (_queue.Handle is null) return 0;

        int errors = 0;

        var queue = (IDXGIInfoQueue*)_queue.Handle;
        ulong count = queue->GetNumStoredMessages(DebugAll);
        for (ulong i = 0; i < count; i++)
        {
            // Size first, then the message: the description is a trailing
            // variable-length blob.
            nuint byteLength = 0;
            if (queue->GetMessageA(DebugAll, i, null, &byteLength) < 0 || byteLength == 0)
                continue;

            byte[] storage = new byte[(int)byteLength];
            fixed (byte* p = storage)
            {
                var message = (InfoQueueMessage*)p;
                if (queue->GetMessageA(DebugAll, i, message, &byteLength) < 0)
                    continue;

                string text = Encoding.ASCII
                    .GetString(message->PDescription, (int)message->DescriptionByteLength)
                    .TrimEnd('\0');

                switch (message->Severity)
                {
                    case InfoQueueMessageSeverity.InfoQueueMessageSeverityCorruption:
                    case InfoQueueMessageSeverity.InfoQueueMessageSeverityError:
                        errors++;
                        logger.LogError("{Backend} DXGI debug layer: {Message}", backend, text);
                        break;
                    case InfoQueueMessageSeverity.InfoQueueMessageSeverityWarning:
                        logger.LogWarning("{Backend} DXGI debug layer: {Message}", backend, text);
                        break;
                    default:
                        logger.LogDebug("{Backend} DXGI debug layer: {Message}", backend, text);
                        break;
                }
            }
        }

        if (count > 0)
            queue->ClearStoredMessages(DebugAll);

        return errors;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Release, not Dispose: Shutdown can run twice and a disposed ComPtr
        // keeps its handle.
        ComOwnership.Release(ref _queue);
    }
}
