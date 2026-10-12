using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scene;
using SpectraEngine.Entities;
using SpectraEngine.Scripting.Luau.Runtime;
using System;

namespace Spectra.Kitchen.Tests;

// One level of a project, played with its scripts and no window: from the
// project's loose files, as the editor plays it, or from the project's pack
// with nothing else mounted, as a game does. Everything past the load is the
// same code for both, so what differs between them came from the content.
internal sealed class PlayedLevel : IDisposable
{
    private readonly FakeRenderer _renderer = new();
    private readonly AssetManager _assets;
    private readonly ProjectContentMount? _mount;
    private readonly SceneManager _manager;
    private readonly PlaySession _session;

    private PlayedLevel(AssetManager assets, ProjectContentMount? mount, Action<Scene, FakeRenderer> load)
    {
        _assets = assets;
        _mount = mount;
        _assets.AttachRenderer(_renderer);

        _manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = Catalog(),
        };
        _manager.Scripts.Sink = Lines;
        _manager.Scripts.RuntimeFactory = LuauScriptRuntime.Create;

        // The level's content is where its scripts are read from: no source is set.
        _manager.LoadStartupScene(_renderer, _assets);
        Scene = _manager.ActiveScene.ShouldNotBeNull();
        load(Scene, _renderer);

        _session = new PlaySession(_manager, new CharacterSimulation(Scene));
    }

    public Scene Scene { get; }

    public ScriptLines Lines { get; } = new();

    // The bundle is named as the project names it: Maps/Level.smap.
    public static PlayedLevel Loose(TempProject project, string bundle)
    {
        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, project.Layout.AssetsPath, hotReloadEnabled: false);

        return new PlayedLevel(assets, mount: null, (scene, renderer) =>
        {
            MapSceneBinder.ApplyTo(project.Layout.LoadMap(bundle), scene, new MapLoadReport());
            scene.RebuildStaticWorld(renderer);
        });
    }

    // The project has been cooked into its own cooked folder.
    public static PlayedLevel Cooked(TempProject project, string bundle)
    {
        ProjectContentMount mount = ProjectContentMount.Open(
            NullLogger.Instance, project.Layout, ContentMountProfile.Shipped);

        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, project.Layout.AssetsPath, mount.Content, hotReloadEnabled: false);

        string compiled = CompiledMapPath.For(bundle);
        return new PlayedLevel(assets, mount, (scene, renderer) =>
        {
            assets.Content.TryOpen(compiled, out ContentBlob? file).ShouldBeTrue(compiled);
            CompiledMapLoader.Load(scene, renderer, file.ShouldNotBeNull(), compiled);
        });
    }

    public PlayedLevel Play(int ticks)
    {
        _session.Enter();
        for (int i = 0; i < ticks; i++)
            _session.Tick(PhysicsDefaults.FixedDeltaTime, default);

        return this;
    }

    public void Dispose()
    {
        _session.Exit();
        _manager.StopEntityWorld();

        // The baked world holds its bytes in the pack, which closes below.
        Scene.ReleaseCompiledStaticWorld(_renderer);
        _assets.ReleaseGraphicsResources();
        _mount?.Dispose();
    }

    // Own catalogue: EntityCatalog.Shared freezes on first read.
    private static EntityCatalog Catalog()
    {
        var catalog = new EntityCatalog();
        catalog.Add(FuncDoor.SpectraSchema, static () => new FuncDoor());
        catalog.Add(TriggerMultiple.SpectraSchema, static () => new TriggerMultiple());
        catalog.Add(LogicRelay.SpectraSchema, static () => new LogicRelay());
        catalog.Add(InfoPlayerStart.SpectraSchema, static () => new InfoPlayerStart());
        return catalog;
    }
}
