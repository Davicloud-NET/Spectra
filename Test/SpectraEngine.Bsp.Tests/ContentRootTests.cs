using SpectraEngine.Core.Assets;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Content root resolution and asset path normalisation.</summary>
public sealed class ContentRootTests
{
    [Fact]
    public void Resolves_to_the_repo_assets_folder_in_a_developer_build()
    {
        ContentRoot.IsDeveloperBuild.ShouldBeTrue(
            "the test assembly runs from bin/ inside the source tree, so the solution file is findable");

        string root = ContentRoot.Path;
        Path.GetFileName(root).ShouldBe(ContentRoot.DirectoryName);
        Path.IsPathRooted(root).ShouldBeTrue();
        Directory.Exists(root).ShouldBeTrue($"content root '{root}' should exist");

        // The repo folder, not the copy under bin/ beside the test binary.
        root.Replace('\\', '/').ShouldNotContain("/bin/");
        File.Exists(Path.Combine(root, "Textures", "dev_grid.png")).ShouldBeTrue();
    }

    [Fact]
    public void Resolution_is_cached_so_repeated_reads_do_not_rewalk_the_tree()
    {
        ReferenceEquals(ContentRoot.Path, ContentRoot.Path).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Textures/dev_grid.png")]
    [InlineData("Textures\\dev_grid.png")]
    [InlineData("./Textures/dev_grid.png")]
    [InlineData("Textures//dev_grid.png")]
    [InlineData("/Textures/dev_grid.png")]
    public void Normalizes_separators_and_noise_segments_to_one_canonical_key(string input)
        => ContentRoot.NormalizeRelativePath(input).ShouldBe("Textures/dev_grid.png");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("./")]
    [InlineData("../Textures/dev_grid.png")]
    [InlineData("Textures/../../secrets.png")]
    public void Rejects_empty_and_escaping_paths(string input)
        => Should.Throw<ArgumentException>(() => ContentRoot.NormalizeRelativePath(input));

    [Fact]
    public void Rejects_rooted_paths()
        => Should.Throw<ArgumentException>(
            () => ContentRoot.NormalizeRelativePath(@"C:\Assets\Textures\dev_grid.png"));

    [Fact]
    public void Resolves_absolute_paths_under_the_given_root()
    {
        string absolute = ContentRoot.ResolveAbsolute(ContentRoot.Path, "Textures\\dev_grid.png");

        Path.IsPathRooted(absolute).ShouldBeTrue();
        File.Exists(absolute).ShouldBeTrue();
        absolute.ShouldStartWith(ContentRoot.Path);
        ContentRoot.ResolveAbsolute(ContentRoot.Path, "Textures/dev_grid.png").ShouldBe(absolute);
    }
}
