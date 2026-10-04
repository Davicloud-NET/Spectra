using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="AssetManager"/> reads content through an
/// <see cref="IContentSource"/> stack, never straight from disk.
/// </summary>
public sealed class ContentSourceTests
{
    private const string PackedMaterial = "Materials/packed.spectramat";
    private const string PackedTexture = "Textures/packed.png";

    [Fact]
    public void A_material_and_its_texture_resolve_from_a_source_the_filesystem_knows_nothing_about()
    {
        string emptyRoot = CreateEmptyContentRoot();
        try
        {
            var packed = new FakeContentSource();
            packed.Add(PackedMaterial, """
                shader = lit
                texture uDiffuse = Textures/packed.png
                color uBaseColor = #FFFFFF
                """);
            packed.Add(
                PackedTexture,
                File.ReadAllBytes(ContentRoot.ResolveAbsolute(ContentRoot.Path, "Textures/dev_grid.png")));

            var stack = new ContentSourceStack();
            stack.Mount(packed);

            var logger = new CapturingLogger();
            // Empty content root: any probe that still goes to disk misses.
            var assets = new AssetManager(logger, emptyRoot, stack, hotReloadEnabled: false);
            assets.AttachRenderer(new FakeRenderer());

            Material material = assets.LoadMaterial(PackedMaterial);

            // The existence check or the parse going to disk gives the default material.
            material.ShouldNotBeSameAs(assets.DefaultMaterial, "the material was read from the mounted source");
            material.SourcePath.ShouldBe(PackedMaterial);

            // The texture probe or the decode going to disk gives the placeholder.
            material.TryGetTexture("uDiffuse", out int unit, out Texture? texture).ShouldBeTrue();
            unit.ShouldBe(0);
            texture.ShouldNotBeSameAs(
                assets.PlaceholderTexture, "the texture slot resolved to real content, not the placeholder");
            ((FakeTexture)texture).Width.ShouldBe(128);
            ((FakeTexture)texture).Format.ShouldBe(TextureFormat.Rgba8);

            packed.Opened.ShouldContain(PackedMaterial);
            packed.Opened.ShouldContain(PackedTexture);
            packed.Probed.ShouldContain(PackedTexture);
            logger.MessagesAt(LogLevel.Error).ShouldBeEmpty(logger.Describe());

            assets.ReleaseGraphicsResources();
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
    }

    [Fact]
    public void A_strict_stack_throws_on_a_total_miss_while_a_lenient_one_returns_false()
    {
        var packed = new FakeContentSource();
        packed.Add(PackedTexture, [1, 2, 3]);

        var lenient = new ContentSourceStack();
        lenient.Mount(packed);
        var strict = new ContentSourceStack(strict: true);
        strict.Mount(packed);

        lenient.TryOpen("Textures/absent.png", out ContentBlob? nothing).ShouldBeFalse();
        nothing.ShouldBeNull();

        Should.Throw<FileNotFoundException>(() => strict.TryOpen("Textures/absent.png", out _));

        strict.TryOpen(PackedTexture, out ContentBlob? found).ShouldBeTrue();
        using (found)
            found.Span.ToArray().ShouldBe(new byte[] { 1, 2, 3 });

        // Exists must not throw even when strict: the asset manager probes
        // with it before choosing a fallback.
        Should.NotThrow(() => strict.Exists("Textures/absent.png")).ShouldBeFalse();
    }

    [Fact]
    public void Watch_paths_come_back_for_loose_content_and_not_for_a_source_that_has_none()
    {
        string root = CreateEmptyContentRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Textures"));
            string file = Path.GetFullPath(Path.Combine(root, "Textures", "watched.png"));
            File.WriteAllBytes(file, [1, 2, 3]);

            var loose = new LooseFileSource(NullLogger<AssetManager>.Instance, root);
            loose.TryGetWatchPath("Textures/watched.png", out string? watchPath).ShouldBeTrue();
            watchPath.ShouldBe(file);
            loose.TryGetWatchPath("Textures/absent.png", out _).ShouldBeFalse();

            var packed = new FakeContentSource();
            packed.Add(PackedTexture, [1, 2, 3]);
            packed.TryGetWatchPath(PackedTexture, out _).ShouldBeFalse();

            var stack = new ContentSourceStack();
            stack.Mount(packed);
            stack.Mount(loose);

            stack.TryGetWatchPath("Textures/watched.png", out string? throughStack).ShouldBeTrue();
            throughStack.ShouldBe(file);
            stack.TryGetWatchPath(PackedTexture, out _).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_highest_priority_source_holding_a_path_answers_and_mount_order_breaks_ties()
    {
        var low = new FakeContentSource(priority: 0);
        low.Add(PackedTexture, [1]);
        var high = new FakeContentSource(priority: 10);
        high.Add(PackedTexture, [2]);
        var alsoLow = new FakeContentSource();
        alsoLow.Add(PackedTexture, [3]);

        var stack = new ContentSourceStack();
        stack.Mount(low);
        stack.Mount(high);
        stack.Mount(alsoLow);

        stack.Count.ShouldBe(3);
        stack.Sources[0].ShouldBeSameAs(high);
        stack.Sources[1].ShouldBeSameAs(low, "equal priorities keep mount order");
        stack.Sources[2].ShouldBeSameAs(alsoLow);

        stack.TryOpen(PackedTexture, out ContentBlob? blob).ShouldBeTrue();
        using (blob)
            blob.Span.ToArray().ShouldBe(new byte[] { 2 });

        high.Opened.ShouldContain(PackedTexture);
        low.Opened.ShouldBeEmpty();
        alsoLow.Opened.ShouldBeEmpty();
    }

    private static string CreateEmptyContentRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "SpectraContentSourceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.GetFullPath(root);
    }

    // In-memory source that records what it was asked for.
    private sealed class FakeContentSource(int priority = 0) : IContentSource
    {
        private readonly Dictionary<string, byte[]> _entries = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Opened { get; } = [];

        public List<string> Probed { get; } = [];

        public int Priority { get; } = priority;

        public void Add(string path, byte[] bytes) => _entries[path] = bytes;

        public void Add(string path, string text) => Add(path, Encoding.UTF8.GetBytes(text));

        public bool TryOpen(string path, [NotNullWhen(true)] out ContentBlob? blob)
        {
            Opened.Add(path);
            if (!_entries.TryGetValue(path, out byte[]? bytes))
            {
                blob = null;
                return false;
            }

            blob = ContentBlob.CopyOf(bytes);
            return true;
        }

        public bool Exists(string path)
        {
            Probed.Add(path);
            return _entries.ContainsKey(path);
        }

        public bool TryGetWatchPath(string path, [NotNullWhen(true)] out string? fullPath)
        {
            fullPath = null;
            return false;
        }

        public void TryEnumerate(string prefix, string extension, List<string> results)
        {
            foreach (string path in _entries.Keys)
            {
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (extension.Length > 0 && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                results.Add(path);
            }
        }

        public override string ToString() => $"fake pack ({_entries.Count} entries)";
    }
}
