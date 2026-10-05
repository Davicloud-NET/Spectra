using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Editor.Sounds;
using System;

namespace SpectraEngine.Editor;

// What an editor session reads its content from: the loose files under the
// content root, and below them the sounds cooked from its WAVs. The engine
// reads cooked sounds only, and a project being edited has none.
internal static class EditorContent
{
    public static ContentSourceStack Mount(
        ILoggerFactory loggerFactory, string contentRoot, string soundCacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        var stack = new ContentSourceStack();
        stack.Mount(new LooseFileSource(loggerFactory.CreateLogger<AssetManager>(), contentRoot));
        stack.Mount(new CookedSoundSource(
            loggerFactory.CreateLogger<CookedSoundSource>(), contentRoot, soundCacheDirectory));

        return stack;
    }
}
