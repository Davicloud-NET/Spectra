using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Bsp.Tests;

// The test thread plays the render thread and FakeRenderer the GPU; only the
// decode runs off-thread. Textures swap in on pump calls from this thread, so
// nothing depends on decode speed.
public sealed class AssetManagerTests
{
    private const string Grid = "Textures/dev_grid.png";
    private const string CheckerGray = "Textures/checker_gray.png";
    private const string Mask = "Textures/gradient_mask.png";

    // Only reached when a decode never lands.
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void Sync_load_uploads_the_decoded_file_to_the_renderer()
    {
        var (assets, renderer) = CreateAttached();

        TextureAsset asset = assets.LoadTexture(Grid, TextureFilter.Nearest, TextureWrap.Clamp);

        asset.IsPlaceholder.ShouldBeFalse();
        asset.RelativePath.ShouldBe(Grid);
        asset.SourcePath.ShouldBe(ContentRoot.ResolveAbsolute(ContentRoot.Path, Grid));

        var texture = asset.Texture.ShouldBeOfType<FakeTexture>();
        texture.Width.ShouldBe(128);
        texture.Height.ShouldBe(128);
        texture.Format.ShouldBe(TextureFormat.Rgba8);
        texture.Filter.ShouldBe(TextureFilter.Nearest);
        texture.Wrap.ShouldBe(TextureWrap.Clamp);
        texture.Pixels.Length.ShouldBe(128 * 128 * 4);

        renderer.LiveTextures.Count.ShouldBe(AssetTestFacts.BuiltInTextures + 1);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Same_path_returns_the_same_instance_and_uploads_once()
    {
        var (assets, renderer) = CreateAttached();

        TextureAsset first = assets.LoadTexture(Grid);
        TextureAsset second = assets.LoadTexture(Grid);
        TextureAsset third = assets.LoadTexture("Textures\\dev_grid.png");

        second.ShouldBeSameAs(first);
        third.ShouldBeSameAs(first);
        second.Texture.ShouldBeSameAs(first.Texture);
        assets.TextureCount.ShouldBe(1);
        renderer.CreatedTextures.Count.ShouldBe(AssetTestFacts.BuiltInTextures + 1);

        TextureAsset other = assets.LoadTexture(CheckerGray);
        other.ShouldNotBeSameAs(first);
        other.Texture.ShouldNotBeSameAs(first.Texture);
        assets.TextureCount.ShouldBe(2);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void TryGet_finds_loaded_assets_and_misses_unloaded_ones()
    {
        var (assets, _) = CreateAttached();

        assets.TryGetTexture(Grid, out _).ShouldBeFalse();

        TextureAsset loaded = assets.LoadTexture(Grid);
        assets.TryGetTexture("Textures\\dev_grid.png", out TextureAsset? found).ShouldBeTrue();
        found.ShouldBeSameAs(loaded);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Async_request_serves_the_placeholder_until_a_pump_swaps_the_real_texture_in()
    {
        var (assets, renderer) = CreateAttached();
        Texture placeholder = assets.PlaceholderTexture.ShouldNotBeNull();

        TextureAsset asset = assets.RequestTexture(Mask, TextureFilter.Linear, TextureWrap.Clamp);

        asset.IsPlaceholder.ShouldBeTrue();
        asset.Texture.ShouldBeSameAs(placeholder);
        asset.Version.ShouldBe(0);
        assets.RequestTexture(Mask, TextureFilter.Linear, TextureWrap.Clamp).ShouldBeSameAs(asset);

        PumpUntil(assets, () => !asset.IsPlaceholder);

        asset.Texture.ShouldNotBeSameAs(placeholder);
        asset.Version.ShouldBe(1);
        var texture = asset.Texture.ShouldBeOfType<FakeTexture>();
        texture.Width.ShouldBe(64);
        texture.Height.ShouldBe(64);
        texture.Format.ShouldBe(TextureFormat.R8);
        texture.Filter.ShouldBe(TextureFilter.Linear);
        texture.Wrap.ShouldBe(TextureWrap.Clamp);

        // The placeholder is shared; the swap must not destroy it.
        ((FakeTexture)placeholder).Disposed.ShouldBeFalse();
        renderer.LiveTextures.ShouldContain((FakeTexture)placeholder);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Pump_is_a_no_op_when_nothing_is_pending()
    {
        var (assets, renderer) = CreateAttached();
        assets.LoadTexture(Grid);

        assets.PumpPendingUploads().ShouldBe(0);
        assets.PumpPendingUploads().ShouldBe(0);
        renderer.CreatedTextures.Count.ShouldBe(AssetTestFacts.BuiltInTextures + 1);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Idle_pump_allocates_nothing()
    {
        var (assets, _) = CreateAttached();
        assets.LoadTexture(Grid);

        // Warm up the JIT and drain what the load left behind.
        for (int i = 0; i < 200; i++) assets.PumpPendingUploads();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) assets.PumpPendingUploads();
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Failed_async_load_keeps_the_placeholder_instead_of_throwing()
    {
        var logger = new CapturingLogger();
        var assets = new AssetManager(logger, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(new FakeRenderer());

        TextureAsset asset = assets.RequestTexture("Textures/does_not_exist.png");

        // A decode failure comes back on the upload queue and is logged by the pump.
        PumpUntil(assets, () => logger.MessagesAt(LogLevel.Error).Count > 0);

        asset.IsPlaceholder.ShouldBeTrue();
        asset.Texture.ShouldBeSameAs(assets.PlaceholderTexture);
        logger.MessagesAt(LogLevel.Error).ShouldContain(
            m => m.Contains("does_not_exist.png"), customMessage: logger.Describe());

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Unload_destroys_the_texture_and_deregisters_it_from_the_renderer()
    {
        var (assets, renderer) = CreateAttached();
        TextureAsset asset = assets.LoadTexture(Grid);
        var texture = (FakeTexture)asset.Texture;

        renderer.LiveTextures.ShouldContain(texture);

        assets.UnloadTexture(Grid).ShouldBeTrue();

        texture.Disposed.ShouldBeTrue();
        renderer.LiveTextures.ShouldNotContain(texture, "DestroyTexture must deregister, not just dispose");
        assets.TryGetTexture(Grid, out _).ShouldBeFalse();
        assets.TextureCount.ShouldBe(0);
        // A handle still held falls back to the placeholder.
        asset.IsPlaceholder.ShouldBeTrue();
        asset.Texture.ShouldBeSameAs(assets.PlaceholderTexture);

        assets.UnloadTexture(Grid).ShouldBeFalse();

        TextureAsset reloaded = assets.LoadTexture(Grid);
        reloaded.ShouldNotBeSameAs(asset);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Releasing_graphics_resources_destroys_everything_the_manager_owns()
    {
        var (assets, renderer) = CreateAttached();
        assets.LoadTexture(Grid);
        assets.LoadTexture(CheckerGray);
        renderer.LiveTextures.Count.ShouldBe(AssetTestFacts.BuiltInTextures + 2);

        assets.ReleaseGraphicsResources();

        renderer.LiveTextures.ShouldBeEmpty();
        renderer.CreatedTextures.ShouldAllBe(t => t.Disposed);
        assets.TextureCount.ShouldBe(0);
        assets.PlaceholderTexture.ShouldBeNull();

        // The engine calls it on both the normal and the crash path.
        Should.NotThrow(assets.ReleaseGraphicsResources);
    }

    [Fact]
    public void Shutdown_after_releasing_on_the_render_thread_is_clean()
    {
        var logger = new CapturingLogger();
        var assets = new AssetManager(logger, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(new FakeRenderer());
        assets.LoadTexture(Grid);

        assets.ReleaseGraphicsResources();
        assets.Shutdown();

        logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty(logger.Describe());
    }

    [Fact]
    public void Shutdown_without_releasing_first_warns_instead_of_touching_the_gpu()
    {
        var logger = new CapturingLogger();
        var assets = new AssetManager(logger, ContentRoot.Path, hotReloadEnabled: false);
        var renderer = new FakeRenderer();
        assets.AttachRenderer(renderer);
        TextureAsset asset = assets.LoadTexture(Grid);

        assets.Shutdown();

        // Shutdown runs on the main thread, which may not destroy GPU textures.
        logger.MessagesAt(LogLevel.Warning).ShouldContain(
            m => m.Contains("ReleaseGraphicsResources"), customMessage: logger.Describe());
        ((FakeTexture)asset.Texture).Disposed.ShouldBeFalse();
    }

    [Fact]
    public void Requesting_a_texture_before_a_renderer_is_attached_fails_loudly()
    {
        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, ContentRoot.Path, hotReloadEnabled: false);

        Should.Throw<InvalidOperationException>(() => assets.RequestTexture(Grid));
        Should.Throw<InvalidOperationException>(() => assets.LoadTexture(Grid));
    }

    [Fact]
    public void Hot_reload_re_decodes_the_changed_file_and_swaps_the_gpu_texture()
    {
        string root = CreateTempContentRoot();
        try
        {
            string file = Path.Combine(root, "Textures", "swap_me.png");
            File.Copy(SourceTexture("checker_gray.png"), file);

            var assets = new AssetManager(NullLogger<AssetManager>.Instance, root, hotReloadEnabled: true);
            var renderer = new FakeRenderer();
            assets.AttachRenderer(renderer);

            TextureAsset asset = assets.LoadTexture("Textures/swap_me.png");
            var original = (FakeTexture)asset.Texture;
            original.Width.ShouldBe(128);
            original.Format.ShouldBe(TextureFormat.Rgb8);
            asset.Version.ShouldBe(1);

            assets.WatchedDirectoryCount.ShouldBe(1);

            // Notify by hand: the watcher event is OS-timed.
            File.Copy(SourceTexture("gradient_mask.png"), file, overwrite: true);
            assets.NotifyFileChanged(file);

            PumpUntil(assets, () => asset.Version > 1);

            asset.Texture.ShouldNotBeSameAs(original);
            asset.IsPlaceholder.ShouldBeFalse();
            var reloaded = (FakeTexture)asset.Texture;
            reloaded.Width.ShouldBe(64);
            reloaded.Height.ShouldBe(64);
            reloaded.Format.ShouldBe(TextureFormat.R8);

            original.Disposed.ShouldBeTrue();
            renderer.LiveTextures.ShouldNotContain(original);
            // Same handle, so materials bound to it follow the swap.
            assets.TryGetTexture("Textures/swap_me.png", out TextureAsset? current).ShouldBeTrue();
            current.ShouldBeSameAs(asset);

            assets.ReleaseGraphicsResources();
        }
        finally
        {
            DeleteTempContentRoot(root);
        }
    }

    [Fact]
    public void Loading_several_textures_from_one_folder_creates_exactly_one_watcher()
    {
        var assets = new AssetManager(NullLogger<AssetManager>.Instance, ContentRoot.Path, hotReloadEnabled: true);
        assets.AttachRenderer(new FakeRenderer());

        assets.LoadTexture(Grid);
        assets.LoadTexture(CheckerGray);
        assets.LoadTexture(Mask);

        assets.WatchedDirectoryCount.ShouldBe(1);

        assets.UnloadTexture(Grid);
        assets.WatchedDirectoryCount.ShouldBe(1, "other assets in the folder are still loaded");

        assets.UnloadTexture(CheckerGray);
        assets.UnloadTexture(Mask);
        assets.WatchedDirectoryCount.ShouldBe(0, "the last asset in the folder was unloaded");

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Decode_landing_after_an_unload_creates_no_orphan_texture()
    {
        var logger = new CapturingLogger();
        var assets = new AssetManager(logger, ContentRoot.Path, hotReloadEnabled: true);
        var renderer = new FakeRenderer();
        assets.AttachRenderer(renderer);

        // The handle leaves the cache while its decode is still on the thread pool.
        const int cycles = 20;
        for (int i = 0; i < cycles; i++)
        {
            assets.RequestTexture(Grid);
            assets.UnloadTexture(Grid).ShouldBeTrue();

            // Drain this cycle's decode first so the count is not timing-dependent.
            int expected = i + 1;
            PumpUntil(assets, () => assets.QueueStatistics.Stale >= expected);
        }

        assets.ReleaseGraphicsResources();

        // ReleaseGraphicsResources only walks the cache, so a texture made for
        // an uncached handle would leak.
        renderer.LiveTextures.ShouldBeEmpty("every GPU texture must be owned by a cached handle");
        renderer.CreatedTextures.ShouldAllBe(t => t.Disposed);
        assets.WatchedDirectoryCount.ShouldBe(0);
    }

    [Fact]
    public void A_failed_async_load_is_retried_by_a_later_request()
    {
        string root = CreateTempContentRoot();
        try
        {
            var logger = new CapturingLogger();
            var assets = new AssetManager(logger, root, hotReloadEnabled: false);
            assets.AttachRenderer(new FakeRenderer());

            // File not there yet, e.g. an art tool still holding the write lock.
            TextureAsset asset = assets.RequestTexture("Textures/late.png");
            PumpUntil(assets, () => logger.MessagesAt(LogLevel.Error).Count > 0);
            asset.IsPlaceholder.ShouldBeTrue();
            asset.LoadFailed.ShouldBeTrue();

            File.Copy(SourceTexture("checker_gray.png"), Path.Combine(root, "Textures", "late.png"));

            assets.RequestTexture("Textures/late.png").ShouldBeSameAs(asset);
            PumpUntil(assets, () => !asset.IsPlaceholder);

            asset.LoadFailed.ShouldBeFalse();
            asset.Version.ShouldBe(1);
            ((FakeTexture)asset.Texture).Width.ShouldBe(128);

            assets.ReleaseGraphicsResources();
        }
        finally
        {
            DeleteTempContentRoot(root);
        }
    }

    [Fact]
    public void A_failed_async_load_is_retried_by_a_later_sync_load()
    {
        string root = CreateTempContentRoot();
        try
        {
            var logger = new CapturingLogger();
            var assets = new AssetManager(logger, root, hotReloadEnabled: false);
            assets.AttachRenderer(new FakeRenderer());

            TextureAsset asset = assets.RequestTexture("Textures/late.png");
            PumpUntil(assets, () => logger.MessagesAt(LogLevel.Error).Count > 0);

            File.Copy(SourceTexture("dev_grid.png"), Path.Combine(root, "Textures", "late.png"));

            TextureAsset loaded = assets.LoadTexture("Textures/late.png");

            loaded.ShouldBeSameAs(asset);
            loaded.IsPlaceholder.ShouldBeFalse();
            loaded.LoadFailed.ShouldBeFalse();
            ((FakeTexture)loaded.Texture).Width.ShouldBe(128);

            assets.ReleaseGraphicsResources();
        }
        finally
        {
            DeleteTempContentRoot(root);
        }
    }

    [Fact]
    public void Repeated_requests_of_a_failing_texture_queue_one_decode_at_a_time()
    {
        var logger = new CapturingLogger();
        var assets = new AssetManager(logger, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(new FakeRenderer());

        TextureAsset asset = assets.RequestTexture("Textures/does_not_exist.png");
        PumpUntil(assets, () => logger.MessagesAt(LogLevel.Error).Count > 0);

        // A frame loop polling a failed handle must not queue a decode per call.
        for (int i = 0; i < 5; i++)
            assets.RequestTexture("Textures/does_not_exist.png").ShouldBeSameAs(asset);

        PumpUntil(assets, () => logger.MessagesAt(LogLevel.Error).Count >= 2);
        logger.MessagesAt(LogLevel.Error).Count.ShouldBeLessThan(
            6, "each request may only retry once the previous decode came back");

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Different_sampler_state_for_one_image_loads_a_separate_texture()
    {
        var (assets, renderer) = CreateAttached();

        TextureAsset sharp = assets.LoadTexture(Grid, TextureFilter.Nearest, TextureWrap.Clamp);
        TextureAsset tiled = assets.LoadTexture(Grid, TextureFilter.LinearMipmap, TextureWrap.Repeat);

        // Sampler state is baked into the GPU texture on every backend.
        tiled.ShouldNotBeSameAs(sharp);
        ((FakeTexture)sharp.Texture).Wrap.ShouldBe(TextureWrap.Clamp);
        ((FakeTexture)sharp.Texture).Filter.ShouldBe(TextureFilter.Nearest);
        ((FakeTexture)tiled.Texture).Wrap.ShouldBe(TextureWrap.Repeat);
        ((FakeTexture)tiled.Texture).Filter.ShouldBe(TextureFilter.LinearMipmap);
        assets.TextureCount.ShouldBe(2);

        assets.LoadTexture(Grid, TextureFilter.Nearest, TextureWrap.Clamp).ShouldBeSameAs(sharp);
        assets.LoadTexture(Grid).ShouldBeSameAs(tiled);
        renderer.CreatedTextures.Count.ShouldBe(AssetTestFacts.BuiltInTextures + 2);

        // A path-only lookup returns the first variant loaded.
        assets.TryGetTexture(Grid, out TextureAsset? first).ShouldBeTrue();
        first.ShouldBeSameAs(sharp);
        assets.TryGetTexture(Grid, TextureFilter.LinearMipmap, TextureWrap.Repeat, out TextureAsset? exact)
            .ShouldBeTrue();
        exact.ShouldBeSameAs(tiled);

        // Unloading the path drops every variant.
        assets.UnloadTexture(Grid).ShouldBeTrue();
        assets.TextureCount.ShouldBe(0);
        ((FakeTexture)renderer.CreatedTextures[AssetTestFacts.BuiltInTextures]).Disposed.ShouldBeTrue();
        ((FakeTexture)renderer.CreatedTextures[AssetTestFacts.BuiltInTextures + 1]).Disposed.ShouldBeTrue();

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Hot_reload_re_decodes_every_sampler_variant_of_the_changed_file()
    {
        string root = CreateTempContentRoot();
        try
        {
            string file = Path.Combine(root, "Textures", "swap_me.png");
            File.Copy(SourceTexture("checker_gray.png"), file);

            var assets = new AssetManager(NullLogger<AssetManager>.Instance, root, hotReloadEnabled: true);
            var renderer = new FakeRenderer();
            assets.AttachRenderer(renderer);

            TextureAsset sharp = assets.LoadTexture("Textures/swap_me.png", TextureFilter.Nearest, TextureWrap.Clamp);
            TextureAsset tiled = assets.LoadTexture("Textures/swap_me.png");

            File.Copy(SourceTexture("gradient_mask.png"), file, overwrite: true);
            assets.NotifyFileChanged(file);

            PumpUntil(assets, () => sharp.Version > 1 && tiled.Version > 1);

            ((FakeTexture)sharp.Texture).Width.ShouldBe(64);
            ((FakeTexture)sharp.Texture).Wrap.ShouldBe(TextureWrap.Clamp);
            ((FakeTexture)tiled.Texture).Width.ShouldBe(64);
            ((FakeTexture)tiled.Texture).Wrap.ShouldBe(TextureWrap.Repeat);

            assets.ReleaseGraphicsResources();
        }
        finally
        {
            DeleteTempContentRoot(root);
        }
    }

    [Fact]
    public void Hot_reload_disabled_registers_no_watchers()
    {
        var (assets, _) = CreateAttached();
        assets.LoadTexture(Grid);

        assets.WatchedDirectoryCount.ShouldBe(0);

        assets.ReleaseGraphicsResources();
    }

    private static (AssetManager Assets, FakeRenderer Renderer) CreateAttached()
    {
        // Hot reload off: a watcher on the shared repo folder only adds OS noise.
        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, ContentRoot.Path, hotReloadEnabled: false);
        var renderer = new FakeRenderer();
        assets.AttachRenderer(renderer);
        return (assets, renderer);
    }

    private static int DroppedDecodes(CapturingLogger logger)
        => logger.MessagesAt(LogLevel.Debug).Count(m => m.Contains("Dropping the decode"));

    private static string SourceTexture(string fileName)
        => ContentRoot.ResolveAbsolute(ContentRoot.Path, $"Textures/{fileName}");

    private static string CreateTempContentRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "SpectraAssetTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Textures"));
        return root;
    }

    // Retries the delete: Windows raises several watcher events per write, so a
    // re-decode on the thread pool can still hold the file after the test passed.
    private static void DeleteTempContentRoot(string root)
    {
        // A time budget, not a retry count: a loaded machine needs longer.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        int delayMs = 5;

        while (true)
        {
            try
            {
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline) throw;

                Thread.Sleep(delayMs);
                delayMs = Math.Min(delayMs * 2, 100);
            }
        }
    }

    private static void PumpUntil(AssetManager assets, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + PumpTimeout;
        while (DateTime.UtcNow < deadline)
        {
            assets.PumpPendingUploads();
            if (condition()) return;
            Thread.Sleep(2);
        }

        throw new TimeoutException($"Condition not met within {PumpTimeout.TotalSeconds:0} s of pumping.");
    }
}
