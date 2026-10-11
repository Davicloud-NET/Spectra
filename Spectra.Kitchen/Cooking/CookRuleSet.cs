using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Scripting;
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
    private readonly SubtitleRule _subtitle = new();
    private readonly CaptionFileRule _captionFile;
    private readonly ScriptRule _script;

    private readonly MapRule _map = new();

    /// <summary>Creates the rules for one project.</summary>
    /// <param name="projectLanguage">
    /// The language the project names as its own. The caption rule compares
    /// every other language with it.
    /// </param>
    /// <param name="scriptCheck">What the script rule asks whether a script compiles.</param>
    public CookRuleSet(string projectLanguage, IScriptSyntaxCheck scriptCheck)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectLanguage);
        ArgumentNullException.ThrowIfNull(scriptCheck);

        _captionFile = new CaptionFileRule(projectLanguage);
        _script = new ScriptRule(scriptCheck);
    }

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
        if (SubtitleRule.Handles(contentPath)) return _subtitle;
        if (CaptionFileRule.Handles(contentPath)) return _captionFile;
        if (ScriptRule.Handles(contentPath)) return _script;

        return _rawCopy;
    }
}
