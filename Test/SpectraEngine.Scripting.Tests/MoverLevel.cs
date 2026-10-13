using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Entities;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

// The demo's course with its start room, door and lift, played through a
// play session with no window. The player moves by the built-in mover until
// a script of the level installs another. The movers the engine ships are
// read from its own Assets folder, beside whatever text a test hands in.
internal sealed class MoverLevel : IDisposable
{
    public const float Dt = PhysicsDefaults.FixedDeltaTime;

    public const string Walk = DemoMover.MoverPath;
    public const string DoubleJump = "Scripts/movers/double_jump.luau";
    public const string Installer = DemoMover.ScriptPath;

    private int _scriptCount;

    // With no course the level is a ground plate and nothing else.
    public MoverLevel(bool hasCourse = true)
    {
        NativeLuau.Require();

        // Own catalogue: EntityCatalog.Shared freezes on first read.
        var catalog = new EntityCatalog();
        catalog.Add(FuncButton.SpectraSchema, static () => new FuncButton());
        catalog.Add(FuncDoor.SpectraSchema, static () => new FuncDoor());
        catalog.Add(FuncMoveLinear.SpectraSchema, static () => new FuncMoveLinear());
        catalog.Add(InfoPlayerStart.SpectraSchema, static () => new InfoPlayerStart());
        catalog.Add(LogicCase.SpectraSchema, static () => new LogicCase());
        catalog.Add(PointSound.SpectraSchema, static () => new PointSound());
        catalog.Add(TriggerMultiple.SpectraSchema, static () => new TriggerMultiple());

        Manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = catalog,
        };
        Manager.Scripts.Sink = Sink;
        Manager.Scripts.Source = Files;
        Manager.Scripts.RuntimeFactory = setup => Runtime = new LuauScriptRuntime(setup);

        var renderer = new FakeRenderer();
        Manager.LoadStartupScene(renderer, new AssetManager(NullLogger<AssetManager>.Instance));
        Scene = Manager.ActiveScene.ShouldNotBeNull();

        if (hasCourse)
            BuildCourse(renderer);

        AddShippedMovers(Files);

        Character = new CharacterSimulation(Scene) { FallOutHeight = DemoPlayArea.FallOutHeight };
        Session = new PlaySession(Manager, Character);
    }

    public SceneManager Manager { get; }

    public Scene Scene { get; }

    public PlaySession Session { get; }

    public CharacterSimulation Character { get; }

    public RecordingSink Sink { get; } = new();

    public TextScriptSource Files { get; } = new();

    // The runtime of the play session that started last.
    public LuauScriptRuntime? Runtime { get; private set; }

    public CharacterState State => Character.State;

    public string[] Printed => Sink.Texts(ScriptSeverity.Print);

    public string[] Errors => Sink.Texts(ScriptSeverity.Error);

    // The movers the engine ships, as its own Assets folder has them.
    public static void AddShippedMovers(TextScriptSource files)
    {
        foreach (string path in new[] { Walk, DoubleJump, Installer })
            files.Add(path, File.ReadAllText(Path.Combine(BindingSourceTests.RepositoryRoot(), "Assets", path)));
    }

    // The course alone. The baseplate was only there to give the manager a scene.
    private void BuildCourse(FakeRenderer renderer)
    {
        for (int i = Scene.Root.Children.Count - 1; i >= 0; i--)
            Scene.Root.RemoveChild(Scene.Root.Children[i]);

        DemoPlayArea.Build(Scene, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default);
        Scene.RebuildStaticWorld(renderer);
    }

    // The script the demo installs its mover with.
    public MoverLevel WithTheShippedMover()
    {
        DemoMover.Build(Scene);
        return this;
    }

    // A script under the root, with its text as a file of its own.
    public MoverLevel WithScript(string text)
    {
        string path = $"Scripts/test{++_scriptCount}.luau";
        Files.Add(path, text);
        Scene.Root.CreateChild($"Script{_scriptCount}").Script = new Script(path);
        return this;
    }

    public MoverLevel Play()
    {
        Session.Enter();
        return this;
    }

    public void Stop() => Session.Exit();

    public MoverLevel Tick(in CharacterCommand command, int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            Session.Tick(Dt, in command);

        return this;
    }

    // The watchdog goes by the wall clock. The short limit holds for the one
    // call that hangs, and the default is back before any other script runs.
    public MoverLevel StopTheNextHangSoon()
    {
        LuauScriptRuntime runtime = Runtime.ShouldNotBeNull();
        runtime.CallLimit = TimeSpan.FromMilliseconds(100);
        Sink.Heard = message =>
        {
            if (message.Severity == ScriptSeverity.Error)
                runtime.CallLimit = LuauHost.DefaultCallLimit;
        };

        return this;
    }

    // Stops the level, which closes the Luau state and its watchdog.
    public void Dispose() => Stop();
}
