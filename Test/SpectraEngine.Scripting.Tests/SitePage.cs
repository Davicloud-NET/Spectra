using System.Text.RegularExpressions;

namespace SpectraEngine.Scripting.Tests;

// One page of the docs site, read from the source tree with line feeds only.
internal sealed partial class SitePage
{
    // Written by the site's own scripts and not committed, so a test that
    // read them would pass on one machine and fail on the next.
    private static readonly string[] Generated = ["reference/api/", "reference/script/"];

    private SitePage(string path, string text)
    {
        Path = path;
        Text = text;
    }

    // From the docs folder, with forward slashes: guides/write-your-first-script.mdx.
    public string Path { get; }

    public string Text { get; }

    public static string DocsFolder =>
        System.IO.Path.Combine(BindingSourceTests.RepositoryRoot(), "site", "src", "content", "docs");

    // Every page a person wrote.
    public static SitePage[] All =>
    [
        .. Directory.EnumerateFiles(DocsFolder, "*.md*", SearchOption.AllDirectories)
            .Select(file => System.IO.Path.GetRelativePath(DocsFolder, file).Replace('\\', '/'))
            .Where(path => !Generated.Any(folder => path.StartsWith(folder, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .Select(At),
    ];

    public static SitePage At(string path) =>
        new(path, File.ReadAllText(System.IO.Path.Combine(DocsFolder, path)).Replace("\r\n", "\n", StringComparison.Ordinal));

    // A fenced block with its language. Inside a list the fence is indented,
    // and so is every line between.
    [GeneratedRegex(
        @"^(?<indent>[ \t]*)```(?<language>\w+)\n(?<body>.*?)\n\k<indent>```[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Fence();

    // The text of every block of one language, in the order of the page.
    public string[] Blocks(string language) =>
    [
        .. Fence().Matches(Text)
            .Where(match => match.Groups["language"].Value == language)
            .Select(match => string.Join('\n', match.Groups["body"].Value.Split('\n')
                .Select(line => line.StartsWith(match.Groups["indent"].Value, StringComparison.Ordinal)
                    ? line[match.Groups["indent"].Length..]
                    : line)) + "\n"),
    ];
}
