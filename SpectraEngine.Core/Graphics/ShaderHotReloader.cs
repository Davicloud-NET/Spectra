using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Watches shader source files and recompiles a changed one into the
/// <see cref="ShaderProgram"/> created from it, so materials holding that
/// program pick up the new code.
/// </summary>
// File events arrive on a pool thread and only enqueue paths. The render
// thread drains them through PumpPendingReloads once per frame.
public sealed class ShaderHotReloader : IDisposable
{
    private readonly ILogger _logger;
    private readonly IShaderCompiler _compiler;
    private readonly GraphicsBackend _backend;
    private readonly Dictionary<string, Registration> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _pending = new();
    private bool _disposed;

    public ShaderHotReloader(ILogger logger, IShaderCompiler compiler, GraphicsBackend backend)
    {
        _logger = logger;
        _compiler = compiler;
        _backend = backend;
    }

    /// <summary>
    /// Registers <paramref name="program"/> for reloads from
    /// <paramref name="absolutePath"/>. Registering a path again replaces the
    /// earlier program.
    /// </summary>
    public void Register(string absolutePath, ShaderProgram program)
    {
        string normalized = Path.GetFullPath(absolutePath);

        // Dispose the old watcher, or it leaks and keeps raising events.
        if (_watchers.TryGetValue(normalized, out Registration previous))
        {
            previous.Watcher.Dispose();
            _logger.LogInformation("Replacing shader watch registration: {Path}", normalized);
        }

        var watcher = new FileSystemWatcher(Path.GetDirectoryName(normalized)!, Path.GetFileName(normalized))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        watcher.Changed += (_, e) => _pending.Enqueue(Path.GetFullPath(e.FullPath));

        _watchers[normalized] = new Registration(program, watcher);
        _logger.LogInformation("Watching shader source: {Path}", normalized);
    }

    /// <summary>Applies every pending reload. Render thread only.</summary>
    public void PumpPendingReloads()
    {
        // One save often raises several Changed events.
        HashSet<string>? seen = null;
        while (_pending.TryDequeue(out string? path))
        {
            seen ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            seen.Add(path);
        }
        if (seen is null) return;

        foreach (string path in seen)
        {
            if (!_watchers.TryGetValue(path, out Registration reg))
                continue;
            TryReload(path, reg.Program);
        }
    }

    private void TryReload(string path, ShaderProgram program)
    {
        // Editors hold a brief write lock while saving, hence the retry.
        string source;
        try
        {
            source = ReadAllTextWithRetry(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Shader reload skipped ({Path}): could not read file ({Message})",
                path, ex.Message);
            return;
        }

        CompiledShaderFile compiled;
        try
        {
            compiled = _compiler.Compile(source, [_backend]);
        }
        catch (Exception ex)
        {
            _logger.LogError("Shader compile failed ({Path}): {Message}", path, ex.Message);
            return;
        }

        var blob = compiled.GetPipeline(_backend);
        if (blob is null)
        {
            _logger.LogError("Shader compile produced no pipeline for {Backend} ({Path})", _backend, path);
            return;
        }

        if (!program.TryReload(blob, out string? error))
        {
            _logger.LogError("Shader reload failed ({Path}): {Error}", path, error);
            return;
        }

        _logger.LogInformation("Shader reloaded: {Path}", path);
    }

    private static string ReadAllTextWithRetry(string path)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(20);
            }
        }
        throw new IOException($"Could not read {path} after retries.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var reg in _watchers.Values)
            reg.Watcher.Dispose();
        _watchers.Clear();
    }

    private readonly record struct Registration(ShaderProgram Program, FileSystemWatcher Watcher);
}
