using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace SpectraEngine.Core.Audio.Acoustics;

/// <summary>
/// Answers what a material is made of, for sound, from its
/// <c>.spectramat</c> file. It reads the file through the content sources and
/// needs no renderer, so a server and a test can ask too. Any thread.
/// </summary>
public sealed class MaterialAcoustics : IAcousticMaterials
{
    private readonly ILogger _logger;
    private readonly IContentSource _content;

    // Taken for every write to _presets, and held while a file is read, so two
    // threads asking for one material read it once and warn once.
    private readonly object _sync = new();

    // By MaterialRef.Id, null where nothing has asked. Replaced whole when it
    // grows, so a hit takes no lock. An answer stays for the run: the engine
    // reads a .spectramat once, and its hot reload covers textures only. The
    // editor does retry a material that failed to load, and Forget is for that.
    private volatile AcousticPreset?[] _presets = [];

    /// <summary>Creates a lookup that reads materials from <paramref name="content"/>.</summary>
    /// <param name="logger">Hears once about each material that could not be read.</param>
    /// <param name="content">The stack the engine reads its materials from.</param>
    public MaterialAcoustics(ILogger logger, IContentSource content)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(content);

        _logger = logger;
        _content = content;
    }

    /// <summary>
    /// The preset <paramref name="material"/> names in its file, or
    /// <see cref="AcousticPresets.Generic"/> when it names none, is missing or
    /// cannot be read. The first call for a material reads its file. Every
    /// later one allocates nothing.
    /// </summary>
    public AcousticPreset Resolve(MaterialRef material)
    {
        int id = material.Id;
        if (id <= 0) return AcousticPresets.Generic;

        AcousticPreset?[] presets = _presets;
        if (id < presets.Length && presets[id] is { } known) return known;

        return ReadAndRemember(material);
    }

    /// <summary>
    /// Drops the answer for <paramref name="material"/>, so the next
    /// <see cref="Resolve"/> reads its file again. Call it where the engine
    /// gives a material another try.
    /// </summary>
    /// <returns>Whether there was an answer to drop.</returns>
    public bool Forget(MaterialRef material)
    {
        lock (_sync)
        {
            AcousticPreset?[] presets = _presets;
            int id = material.Id;
            if (id <= 0 || id >= presets.Length || presets[id] is null) return false;

            presets[id] = null;
            return true;
        }
    }

    private AcousticPreset ReadAndRemember(MaterialRef material)
    {
        // An id the registry never gave out. Not remembered, so it cannot grow the table.
        if (!MaterialRegistry.TryGetPath(material, out string path)) return AcousticPresets.Generic;

        lock (_sync)
        {
            AcousticPreset?[] presets = _presets;
            int id = material.Id;
            if (id < presets.Length && presets[id] is { } raced) return raced;

            AcousticPreset preset = Read(path);

            if (id < presets.Length)
            {
                presets[id] = preset;
                return preset;
            }

            // Room for every material interned so far, so a level's worth grows it once.
            var grown = new AcousticPreset?[Math.Max(MaterialRegistry.Count + 1, id + 1)];
            presets.CopyTo(grown, 0);
            grown[id] = preset;
            _presets = grown;
            return preset;
        }
    }

    private AcousticPreset Read(string path)
    {
        if (TryParse(path, out MaterialDefinition? definition, out string problem))
            return definition.Acoustic ?? AcousticPresets.Generic;

        _logger.LogWarning("Material {Path} {Problem}; using the generic acoustic preset", path, problem);
        return AcousticPresets.Generic;
    }

    // The probe and the open ask the same source with the same path, as AssetManager does.
    private bool TryParse(string path, [NotNullWhen(true)] out MaterialDefinition? definition, out string problem)
    {
        definition = null;

        string key;
        try
        {
            key = ContentRoot.NormalizeRelativePath(path);
        }
        catch (ArgumentException ex)
        {
            problem = $"is not a usable path ({ex.Message})";
            return false;
        }

        if (!_content.Exists(key))
        {
            problem = "not found";
            return false;
        }

        try
        {
            if (!_content.TryOpen(key, out ContentBlob? blob))
            {
                problem = "could not be read";
                return false;
            }

            using (blob) definition = MaterialParser.ParseUtf8(blob.Span, key);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // A strict stack throws on a miss, and a source that makes its content may refuse the file.
            problem = $"could not be read ({ex.Message})";
            return false;
        }

        problem = "has no line the parser could use";
        return !IsUnusable(definition);
    }

    // The parser never fails. A file that is not a material comes back as warnings and nothing else.
    private static bool IsUnusable(MaterialDefinition definition) =>
        definition.Warnings.Count > 0 &&
        definition.Acoustic is null &&
        definition.ShaderName is null &&
        definition.Textures.Count == 0 &&
        definition.Parameters.Count == 0;
}
