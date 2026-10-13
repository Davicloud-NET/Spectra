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
using System.Collections.Generic;

namespace Spectra.Kitchen.Tests;

// The demo's course of a project, walked by commands with its scripts and
// no window: from the project's loose files, or from its pack with nothing
// else mounted. Everything past the load is the same code for both.
internal sealed class PlayedCourse : IDisposable
{
    private readonly FakeRenderer _renderer = new();
    private readonly AssetManager _assets;
    private readonly ProjectContentMount? _mount;
    private readonly SceneManager _manager;
    private readonly PlaySession _session;

    private PlayedCourse(AssetManager assets, ProjectContentMount? mount, Action<Scene, FakeRenderer> load)
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
        for (int i = Scene.Root.Children.Count - 1; i >= 0; i--)
            Scene.Root.RemoveChild(Scene.Root.Children[i]);

        load(Scene, _renderer);
        Character = new CharacterSimulation(Scene) { FallOutHeight = DemoPlayArea.FallOutHeight };
        _session = new PlaySession(_manager, Character);
    }

    public Scene Scene { get; }

    public CharacterSimulation Character { get; }

    public ScriptLines Lines { get; } = new();

    // Whether the content the level plays from has a file, as its scripts ask for one.
    public bool Has(string contentPath) => _assets.Content.Exists(contentPath);

    public static PlayedCourse Loose(TempProject project, string bundle)
    {
        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, project.Layout.AssetsPath, hotReloadEnabled: false);

        return new PlayedCourse(assets, mount: null, (scene, renderer) =>
        {
            MapSceneBinder.ApplyTo(project.Layout.LoadMap(bundle), scene, new MapLoadReport());
            scene.RebuildStaticWorld(renderer);
        });
    }

    // The project has been cooked into its own cooked folder.
    public static PlayedCourse Cooked(TempProject project, string bundle)
    {
        ProjectContentMount mount = ProjectContentMount.Open(
            NullLogger.Instance, project.Layout, ContentMountProfile.Shipped);

        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, project.Layout.AssetsPath, mount.Content, hotReloadEnabled: false);

        string compiled = CompiledMapPath.For(bundle);
        return new PlayedCourse(assets, mount, (scene, renderer) =>
        {
            assets.Content.TryOpen(compiled, out ContentBlob? file).ShouldBeTrue(compiled);
            CompiledMapLoader.Load(scene, renderer, file.ShouldNotBeNull(), compiled);
        });
    }

    // Starts the level and walks the legs. The state after every tick.
    public List<CharacterState> Walk(IEnumerable<(CharacterCommand Command, int Ticks)> legs)
    {
        _session.Enter();

        var states = new List<CharacterState>();
        foreach ((CharacterCommand command, int ticks) in legs)
        {
            for (int i = 0; i < ticks; i++)
            {
                _session.Tick(PhysicsDefaults.FixedDeltaTime, in command);
                states.Add(Character.State);
            }
        }

        return states;
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

    // Own catalogue: EntityCatalog.Shared freezes on first read. The classes
    // the course is built with.
    private static EntityCatalog Catalog()
    {
        var catalog = new EntityCatalog();
        catalog.Add(FuncButton.SpectraSchema, static () => new FuncButton());
        catalog.Add(FuncDoor.SpectraSchema, static () => new FuncDoor());
        catalog.Add(FuncMoveLinear.SpectraSchema, static () => new FuncMoveLinear());
        catalog.Add(InfoPlayerStart.SpectraSchema, static () => new InfoPlayerStart());
        catalog.Add(LogicCase.SpectraSchema, static () => new LogicCase());
        catalog.Add(PointSound.SpectraSchema, static () => new PointSound());
        catalog.Add(TriggerMultiple.SpectraSchema, static () => new TriggerMultiple());
        return catalog;
    }
}
