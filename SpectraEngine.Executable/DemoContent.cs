using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;

namespace SpectraEngine.Executable;

// Where the demo reads its own content from when it is given no project.
internal static class DemoContent
{
    // The folder beside the executable that the build cooks the demo's sounds
    // into. Not inside the content root: a project exported from the demo
    // copies that folder, and a cook refuses a sound that is there twice.
    public const string CookedSoundsDirectoryName = "CookedSounds";

    public static ContentSourceStack Mount(ILogger logger) =>
        Mount(logger, ContentRoot.Path, Path.Combine(AppContext.BaseDirectory, CookedSoundsDirectoryName));

    // The demo has no cook in it and the engine reads cooked sounds only, so
    // the sounds the build cooked are mounted below the content root.
    public static ContentSourceStack Mount(ILogger logger, string contentRoot, string cookedSounds)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var stack = new ContentSourceStack();
        stack.Mount(new LooseFileSource(logger, contentRoot));
        stack.Mount(new LooseFileSource(logger, cookedSounds, priority: -1));

        return stack;
    }
}
