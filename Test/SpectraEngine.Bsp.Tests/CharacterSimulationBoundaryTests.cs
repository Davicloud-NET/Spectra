using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// A character must simulate with no camera, input device or renderer: a
// dedicated server, rollback and a scripted mover all need that.
// Scene.RebuildStaticWorld still takes a Renderer, so the world is not headless yet.
public sealed class CharacterSimulationBoundaryTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    // Matched by name, so the test need not reference graphics or input.
    private static readonly string[] Forbidden =
    [
        "Camera", "InputManager", "ICursorLock", "Renderer", "DebugDraw", "RenderView",
    ];

    [Fact]
    public void The_simulation_names_no_rendering_or_input_type_anywhere_in_its_surface()
    {
        Type type = typeof(CharacterSimulation);

        var referenced = type.GetConstructors()
            .SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
            .Concat(type.GetProperties().Select(p => p.PropertyType))
            .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
            .Select(t => t.Name.TrimEnd('&'))
            .Distinct()
            .ToArray();

        string[] violations = referenced.Where(n => Forbidden.Contains(n)).ToArray();

        Assert.True(violations.Length == 0,
            $"CharacterSimulation's public surface names {string.Join(", ", violations)}. " +
            "A simulation that needs a camera cannot run on a server, in a replay, or under a script.");
    }

    [Fact]
    public void A_character_can_be_spawned_walked_and_respawned_with_no_view_of_any_kind()
    {
        var scene = new Scene("Headless");

        SceneNode floor = scene.Root.CreateChild("floor");
        floor.LocalPosition = new Vector3(0f, -0.5f, 0f);
        floor.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(
            new Vector3(-4f, -0.5f, -4f), new Vector3(4f, 0.5f, 4f), MaterialRef.Default);

        scene.RebuildStaticWorld(new FakeRenderer());

        var simulation = new CharacterSimulation(scene)
        {
            SpawnPosition = new Vector3(0f, 0.05f, 0f),
            FallOutHeight = -20f,
        };
        simulation.Spawn();

        for (int i = 0; i < 30; i++)
            Assert.False(simulation.Tick(default, Dt));

        Assert.True(simulation.State.Grounded, "the character should be standing on the floor");
        Assert.Equal(simulation.Tuning.SkinWidth, simulation.State.Position.Y, 4);

        // Walk east off the 4-unit slab until the fall guard fires.
        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = 0f };

        bool respawned = false;
        for (int i = 0; i < 400 && !respawned; i++)
            respawned = simulation.Tick(in walk, Dt);

        Assert.True(respawned, "walking off the slab should eventually trip the fall-out guard");
        Assert.Equal(1, simulation.Respawns);
        Assert.Equal(simulation.SpawnPosition, simulation.State.Position);
    }

    [Fact]
    public void State_can_be_captured_and_restored_as_a_plain_struct_copy()
    {
        // Network correction and rollback both restore by copy. That only
        // works while CharacterState holds no references.
        var scene = new Scene("Restore");
        SceneNode floor = scene.Root.CreateChild("floor");
        floor.LocalPosition = new Vector3(0f, -0.5f, 0f);
        floor.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(
            new Vector3(-8f, -0.5f, -8f), new Vector3(8f, 0.5f, 8f), MaterialRef.Default);
        scene.RebuildStaticWorld(new FakeRenderer());

        var simulation = new CharacterSimulation(scene) { SpawnPosition = new Vector3(0f, 0.05f, 0f) };
        simulation.Spawn();
        for (int i = 0; i < 20; i++)
            simulation.Tick(default, Dt);

        CharacterState captured = simulation.State;

        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = 0f };
        for (int i = 0; i < 40; i++)
            simulation.Tick(in walk, Dt);

        Assert.NotEqual(captured.Position.X, simulation.State.Position.X, 3);

        simulation.Restore(in captured);
        Assert.Equal(captured.Position, simulation.State.Position);
        Assert.Equal(captured.Velocity, simulation.State.Velocity);

        Vector3 firstRun = default;
        for (int i = 0; i < 40; i++)
            simulation.Tick(in walk, Dt);
        firstRun = simulation.State.Position;

        simulation.Restore(in captured);
        for (int i = 0; i < 40; i++)
            simulation.Tick(in walk, Dt);

        Assert.Equal(firstRun, simulation.State.Position);
    }
}
