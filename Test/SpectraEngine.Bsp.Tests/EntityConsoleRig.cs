using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// A scene manager, a console and a watch joined the way an engine joins them:
// the watch's trace goes to the scene manager, which gives it to each world.
// Own catalogue: EntityCatalog.Shared freezes on first read.
internal sealed class EntityConsoleRig
{
    private const float TickSeconds = 1f / 60f;

    public EntityConsoleRig()
    {
        var catalog = new EntityCatalog();
        catalog.Add(new EntitySchema("relay", inputs: ["Trigger"], outputs: ["OnTrigger"]), () => new RelayEntity());
        catalog.Add(CounterEntity.Schema, () => new CounterEntity());
        catalog.Add(new EntitySchema("recorder"), () => new RecordingEntity(Received));
        catalog.Add(
            new EntitySchema("spawner", outputs: [SpawnFiringEntity.OnSpawned]),
            () => new SpawnFiringEntity());

        Manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = catalog,
        };
        Manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        Scene = Manager.ActiveScene.ShouldNotBeNull();

        Watch = new EntityWatch(Console.Output);
        EntityConsoleCommands.Register(Console.Commands, Watch);
        Watch.Changed += () => Manager.EntityTrace = Watch.ActiveTrace;
    }

    public SceneManager Manager { get; }

    public Scene Scene { get; }

    public SpectraConsole Console { get; } = new();

    public EntityWatch Watch { get; }

    // What every recorder entity was sent, in order.
    public List<string> Received { get; } = [];

    public EntityWorld World =>
        Manager.EntityWorld ?? throw new InvalidOperationException("The rig's level is not running.");

    public SceneNode Place(string name, string className) => EntityRuntime.Place(Scene.Root, name, className);

    public void Play() => Manager.StartEntityWorld();

    public void Stop() => Manager.StopEntityWorld();

    // Runs a line as a frame's drain would and returns what it printed.
    public string[] Run(string line) => Texts(RunLines(line));

    public IReadOnlyList<ConsoleLine> RunLines(string line, bool? isPlaying = null)
    {
        EntityWorld? world = Manager.EntityWorld;
        Console.Execute(line, new ConsoleFrame(Scene, world, isPlaying ?? world is not null));
        return Console.Output.Drain();
    }

    // Returns what the ticks printed.
    public string[] Tick(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            World.Tick(TickSeconds);

        return Printed();
    }

    // Ticks until something is printed and returns it.
    public string[] TickUntilPrinted(int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            World.Tick(TickSeconds);
            if (Console.Output.Count > 0)
                return Printed();
        }

        throw new InvalidOperationException($"Nothing was printed in {limit} ticks.");
    }

    public string[] Printed() => Texts(Console.Output.Drain());

    private static string[] Texts(IReadOnlyList<ConsoleLine> lines) => [.. lines.Select(line => line.Text)];
}
