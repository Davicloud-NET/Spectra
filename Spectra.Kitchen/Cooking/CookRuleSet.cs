using Spectra.Kitchen.Rules;
using System;

namespace Spectra.Kitchen.Cooking;

/// <summary>
/// Decides which rule cooks which asset. A file with no rule is copied raw.
/// </summary>
// Hand-written table. Finding rules by reflection would be trimmed away in an
// AOT build and every asset would fall through to the raw copy.
public sealed class CookRuleSet
{
    private readonly RawCopyRule _rawCopy = new();
    private readonly ShaderRule _shader = new();
    private readonly ImageRule _image = new();
    private readonly MaterialRule _material = new();
    private readonly AudioRule _audio = new();
    private readonly ModelRule _model = new();

    private readonly MapRule _map = new();

    /// <summary>
    /// The rule that bakes a map bundle. A bundle is a folder, so it does not go
    /// through <see cref="Resolve"/>.
    /// </summary>
    public IRule ResolveMap() => _map;

    /// <summary>The rule that cooks <paramref name="contentPath"/>.</summary>
    public IRule Resolve(string contentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        if (ShaderRule.Handles(contentPath)) return _shader;
        if (ImageRule.Handles(contentPath)) return _image;
        if (MaterialRule.Handles(contentPath)) return _material;
        if (AudioRule.Handles(contentPath)) return _audio;
        if (ModelRule.Handles(contentPath)) return _model;

        return _rawCopy;
    }
}
