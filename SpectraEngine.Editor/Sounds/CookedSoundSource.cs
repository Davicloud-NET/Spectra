using Microsoft.Extensions.Logging;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace SpectraEngine.Editor.Sounds;

// Makes a loose WAV answer for its cooked name, so the editor plays the bytes
// a cook would ship. Asked for Sounds/x.saudio where the content root has only
// Sounds/x.wav, it cooks the WAV with the cook's own rule and keeps the result
// in a cache outside the project.
internal sealed class CookedSoundSource : IContentSource
{
    // Below a loose folder at its default priority, so a cooked file the
    // project has itself wins.
    public const int BelowLooseFiles = -1;

    // Logged when a sound asked for before comes back from changed files. The
    // shell drops its problems about that sound on it.
    public const string CookedAgainTemplate = "Sound {Path} changed and was cooked again";

    private readonly ILogger _logger;
    private readonly string _contentRoot;
    private readonly SoundCookCache _cache;
    private readonly CookSettings _settings = new();

    // One per sound, so two callers asking at once cook it once.
    private readonly ConcurrentDictionary<string, object> _gates = new(StringComparer.OrdinalIgnoreCase);

    // The cook key each sound's diagnostics were last logged under. Every
    // entity that names a broken sound asks for it, and one telling is enough.
    private readonly ConcurrentDictionary<string, UInt128> _said = new(StringComparer.OrdinalIgnoreCase);

    private int _cookCount;

    public CookedSoundSource(
        ILogger logger, string contentRoot, string cacheDirectory, int priority = BelowLooseFiles)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        _logger = logger;
        _contentRoot = Path.GetFullPath(contentRoot);
        _cache = new SoundCookCache(cacheDirectory);
        Priority = priority;
    }

    public int Priority { get; }

    // How many sounds were cooked, as opposed to read back from the cache.
    public int CookCount => Volatile.Read(ref _cookCount);

    public bool Exists(string path) => LooseSoundCook.FindSource(_contentRoot, path) is not null;

    // Cooks when the cache has nothing for the sound's files as they are now.
    // Throws InvalidDataException with the cook's message for a WAV the cook
    // refuses. That is not a miss: the sound is there and cannot be played.
    public bool TryOpen(string path, [NotNullWhen(true)] out ContentBlob? blob)
    {
        blob = null;
        if (LooseSoundCook.FindSource(_contentRoot, path) is not { } sound) return false;

        lock (_gates.GetOrAdd(path, static _ => new object()))
        {
            blob = OpenCached(path, sound) ?? Cook(path, sound);
        }

        return true;
    }

    public bool TryGetWatchPath(string path, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = LooseSoundCook.FindSource(_contentRoot, path) is { } sound
            ? ContentRoot.ResolveAbsolute(_contentRoot, sound)
            : null;

        return fullPath is not null;
    }

    public void TryEnumerate(string prefix, string extension, List<string> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (!string.IsNullOrEmpty(extension) && !AudioContentPath.IsCooked(extension)) return;

        string folder = _contentRoot;
        try
        {
            if (!string.IsNullOrEmpty(prefix)) folder = ContentRoot.ResolveAbsolute(_contentRoot, prefix);
            if (!Directory.Exists(folder)) return;

            // A .wav and a .wave of one name cook to the same path.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!AudioRule.Handles(file)) continue;

                string cooked = AudioContentPath.CookedPathFor(Path.GetRelativePath(_contentRoot, file));
                if (seen.Add(cooked)) results.Add(cooked);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Keep what was found so far.
            _logger.LogWarning("Listing the sounds under '{Prefix}' stopped early: {Message}", prefix, ex.Message);
        }
    }

    public override string ToString() => $"sounds cooked on first use @ {_cache.Root}";

    // Null when the cache has nothing usable for the files as they are now.
    private ContentBlob? OpenCached(string cookedPath, string sound)
    {
        using SoundCacheEntry? entry = _cache.TryOpen(cookedPath);
        if (entry is null || !entry.Stamp.Holds(_contentRoot, sound, _settings)) return null;

        LooseSoundStamp stamp = entry.Stamp;
        if (!stamp.HasSound)
        {
            Say(sound, stamp.Key, stamp.Diagnostics);
            throw Refusal(sound, stamp.Diagnostics);
        }

        ContentBlob? cached = entry.ReadSound();
        if (cached is not null) Say(sound, stamp.Key, stamp.Diagnostics);

        return cached;
    }

    private ContentBlob Cook(string cookedPath, string sound)
    {
        long started = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _cookCount);

        LooseSoundResult result;
        try
        {
            result = LooseSoundCook.Run(_contentRoot, sound, _settings);
        }
        // A boundary: a fault in the cook fails this one load, not the editor.
        catch (Exception fault)
        {
            _logger.LogWarning("The cook failed on sound {Path}: {Message}", sound, fault.Message);
            throw new InvalidDataException($"The cook failed on '{sound}': {fault.Message}", fault);
        }

        Say(sound, result.Key, result.Diagnostics);
        if (result.IsRepeatable) Keep(cookedPath, sound, result);

        if (result.Cooked is not { } cooked) throw Refusal(sound, result.Diagnostics);

        _logger.LogInformation(
            "Cooked sound {Path} for the editor in {Milliseconds:0} ms",
            sound, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return ContentBlob.CopyOf(cooked);
    }

    // A cache that cannot be written costs a cook at the next start, no more.
    private void Keep(string cookedPath, string sound, LooseSoundResult result)
    {
        try
        {
            _cache.Write(cookedPath, result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "Sound {Path} was cooked and could not be kept in '{Cache}', so it is cooked again at the " +
                "next start: {Message}",
                sound, _cache.Root, ex.Message);
        }
    }

    // Warnings, not errors, also for a refusal: a sound that cannot play is a
    // content problem, and the level still runs. The code is a property of its
    // own, so the shell keeps two codes about one sound apart.
    private void Say(string sound, UInt128 key, IReadOnlyList<CookDiagnostic> diagnostics)
    {
        if (_said.TryGetValue(sound, out UInt128 said))
        {
            if (said == key) return;

            _logger.LogInformation(CookedAgainTemplate, sound);
        }

        _said[sound] = key;

        foreach (CookDiagnostic diagnostic in diagnostics)
        {
            LogLevel level = diagnostic.Severity == CookDiagnosticSeverity.Info ? LogLevel.Debug : LogLevel.Warning;
            _logger.Log(level, "Sound {Path}: {CookCode}: {Message}", sound, diagnostic.Id.ToString(), diagnostic.Message);
        }
    }

    private static InvalidDataException Refusal(string sound, IReadOnlyList<CookDiagnostic> diagnostics)
    {
        foreach (CookDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.IsError) return new InvalidDataException($"{diagnostic.Id}: {diagnostic.Message}");
        }

        return new InvalidDataException($"The cook wrote nothing for '{sound}'");
    }
}
