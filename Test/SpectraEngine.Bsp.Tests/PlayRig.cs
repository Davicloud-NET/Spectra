using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// The baseplate level with a keyboard, a first-person view and a session,
// driven frame by frame the way the engine loop drives them. No window.
// The character spawns at the origin looking down +x.
internal sealed class PlayRig
{
    public const float Dt = PhysicsDefaults.FixedDeltaTime;

    public PlayRig()
    {
        // Own catalogue: EntityCatalog.Shared freezes on first read.
        Manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = EntityRuntime.Catalog(Log),
        };
        Manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        Scene = Manager.ActiveScene.ShouldNotBeNull();

        Input = new InputManager(NullLogger<InputManager>.Instance);
        View = new FirstPersonController(NullLogger.Instance, Scene, Input)
        {
            SpawnPosition = Manager.PlayerSpawn,
            SpawnYaw = Manager.PlayerSpawnYaw,
            FallOutHeight = Manager.PlayerFallOutHeight,
        };
        Session = new PlaySession(Manager, View.Simulation);
    }

    // One line per input a recorder entity got.
    public List<string> Log { get; } = [];

    public SceneManager Manager { get; }

    public Scene Scene { get; }

    public InputManager Input { get; }

    public FirstPersonController View { get; }

    public PlaySession Session { get; }

    public CharacterSimulation Character => View.Simulation;

    public EntityWorld Entities => Manager.EntityWorld.ShouldNotBeNull();

    public Vector3 Eye => Character.State.Position + new Vector3(0f, Character.Tuning.EyeHeight, 0f);

    // A part brush box. Place what the level needs before Play.
    public SceneNode Part(string name, Vector3 center, Vector3 half, SceneNode? parent = null)
    {
        SceneNode node = (parent ?? Scene.Root).CreateChild(name);
        node.LocalPosition = center;

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(-half, half);
        return node;
    }

    // A part that logs the inputs it gets, as an entity named like its node.
    public SceneNode Recorder(string name, Vector3 center, Vector3 half)
    {
        SceneNode node = Part(name, center, half);
        node.Entity = new EntityData("recorder");
        return node;
    }

    // Starts play and lets the character land.
    public void Play()
    {
        Session.Enter();
        View.Enter();

        for (int i = 0; i < 60; i++)
            Frame(ticks: 1);

        Character.State.Grounded.ShouldBeTrue();
    }

    // One frame of the engine loop: sample input, run the ticks, place the view.
    public void Frame(int ticks, float alpha = 0f)
    {
        Input.Update(Dt);
        View.BeginFrame(Dt);

        CharacterCommand command = View.Command;
        for (int i = 0; i < ticks; i++)
        {
            PlayTickResult result = Session.Tick(Dt, in command);
            View.OnTick(in result);
        }

        View.UpdateView(Dt, alpha);
    }

    // Ticks with a command of the test's own, as a server or a replay would.
    public void Tick(in CharacterCommand command, int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            Session.Tick(Dt, in command);
    }
}
