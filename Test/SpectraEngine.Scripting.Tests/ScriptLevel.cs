using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

// The baseplate level with scripts on it, played through a play session the
// way the engine loop plays one. No window. A script is text the test hands
// in, and what scripts print is kept in the order it came.
internal sealed class ScriptLevel : IDisposable
{
    public const float Dt = PhysicsDefaults.FixedDeltaTime;

    private int _scriptCount;

    // With no character nothing walks, as on a server.
    public ScriptLevel(bool hasCharacter = true)
    {
        NativeLuau.Require();

        // Own catalogue: EntityCatalog.Shared freezes on first read.
        var catalog = new EntityCatalog();
        catalog.Add(new EntitySchema(SayingEntity.Class), () => new SayingEntity(Sink));

        Manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = catalog,
        };
        Manager.Scripts.Sink = Sink;
        Manager.Scripts.Source = Files;
        Manager.Scripts.RuntimeFactory = setup =>
        {
            Runtime = new LuauScriptRuntime(setup);
            Runtimes.Add(Runtime);
            return Runtime;
        };

        Manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        Scene = Manager.ActiveScene.ShouldNotBeNull();
        Session = new PlaySession(Manager, hasCharacter ? new CharacterSimulation(Scene) : null);
    }

    public SceneManager Manager { get; }

    public Scene Scene { get; }

    public PlaySession Session { get; }

    public RecordingSink Sink { get; } = new();

    public TextScriptSource Files { get; } = new();

    // The runtime of the play session that started last.
    public LuauScriptRuntime? Runtime { get; private set; }

    public List<LuauScriptRuntime> Runtimes { get; } = [];

    public EntityWorld Entities => Manager.EntityWorld.ShouldNotBeNull();

    // The level's ground: a world brush.
    public SceneNode Baseplate => Scene.Root.Children.Single(node => node.Name == "Baseplate");

    public string[] Printed => Sink.Texts(ScriptSeverity.Print);

    public string[] Errors => Sink.Texts(ScriptSeverity.Error);

    // A part brush box, half a unit to a side.
    public SceneNode Part(string name, Vector3 center, SceneNode? parent = null)
    {
        SceneNode node = (parent ?? Scene.Root).CreateChild(name);
        node.LocalPosition = center;

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(new Vector3(-0.25f), new Vector3(0.25f));
        return node;
    }

    // A script node under a parent, with its text as a file of its own.
    public SceneNode AddScript(SceneNode parent, string text, string name = "Script")
    {
        string path = $"Scripts/{name}{++_scriptCount}.luau";
        Files.Add(path, text);

        SceneNode node = parent.CreateChild(name);
        node.Script = new Script(path);
        return node;
    }

    // Starts the level. With a character the session does it. With none, the
    // entities are started the way a host with nobody walking would.
    public ScriptLevel Play()
    {
        if (Session.Character is null)
            Manager.StartEntityWorld();
        else
            Session.Enter();

        return this;
    }

    public void Stop()
    {
        Session.Exit();
        Manager.StopEntityWorld();
    }

    public ScriptLevel Tick(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            Session.Tick(Dt, default);

        return this;
    }

    // Plays a level whose one script is this text, for a tick, and hands back
    // what it printed.
    public static string[] Run(string text)
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Part("Part", new Vector3(0f, 2f, 0f)), text);
        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        return level.Printed;
    }

    // Stops the level, which closes the Luau state and its watchdog.
    public void Dispose() => Stop();
}
