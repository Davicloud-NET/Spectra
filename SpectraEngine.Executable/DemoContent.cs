using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;

namespace SpectraEngine.Executable;

// Where the demo reads its own content from when it is given no project.
internal static class DemoContent
{
    // A developer build reads the repo's Assets folder, so an edited file
    // reloads. The build cooks the demo's sounds into the copy beside the
    // executable, so that copy is mounted below it. In a published build the
    // two are one folder.
    public static ContentSourceStack Mount(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var stack = new ContentSourceStack();
        stack.Mount(new LooseFileSource(logger, ContentRoot.Path));

        string built = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ContentRoot.DirectoryName));
        StringComparison comparison =
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!string.Equals(built, Path.GetFullPath(ContentRoot.Path), comparison))
            stack.Mount(new LooseFileSource(logger, built, priority: -1));

        return stack;
    }
}
