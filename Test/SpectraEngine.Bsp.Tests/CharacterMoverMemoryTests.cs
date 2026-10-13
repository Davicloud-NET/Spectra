using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The numbers a replaced mover keeps between ticks. They are part of the
/// state, so they go where the state goes: zero when the character is
/// placed, copied with a restore, and left alone by the built-in mover.
/// </summary>
public sealed class CharacterMoverMemoryTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    [Fact]
    public void A_number_kept_in_a_slot_is_read_back_and_the_other_slots_stay_zero()
    {
        var memory = default(CharacterMoverMemory);

        memory[3] = 2.5f;

        memory[3].ShouldBe(2.5f);
        for (int i = 0; i < CharacterMoverMemory.Capacity; i++)
        {
            if (i != 3)
                memory[i].ShouldBe(0f, $"slot {i}");
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(CharacterMoverMemory.Capacity)]
    public void A_slot_that_is_not_there_is_refused(int index)
    {
        var memory = default(CharacterMoverMemory);

        Should.Throw<ArgumentOutOfRangeException>(() => memory[index]);
        Should.Throw<ArgumentOutOfRangeException>(() => memory[index] = 1f);
    }

    // A replay has to give the same bits, and to a float -0 is 0.
    [Fact]
    public void Two_memories_are_equal_when_every_slot_has_the_same_bits()
    {
        var one = default(CharacterMoverMemory);
        var other = default(CharacterMoverMemory);
        one[7] = 4f;
        other[7] = 4f;

        (one == other).ShouldBeTrue();
        one.GetHashCode().ShouldBe(other.GetHashCode());

        other[0] = -0f;

        (one != other).ShouldBeTrue();
        one.Equals((object)other).ShouldBeFalse();
    }

    [Fact]
    public void The_built_in_mover_leaves_the_memory_as_it_finds_it()
    {
        CharacterSimulation simulation = OnAFloor();
        CharacterState state = simulation.State;
        state.Memory[0] = 3f;
        state.Memory[7] = -1.5f;
        simulation.Restore(in state);

        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Buttons = CharacterButtons.Jump };
        for (int i = 0; i < 60; i++)
            simulation.Tick(in walk, Dt);

        simulation.State.Memory.ShouldBe(state.Memory);
        simulation.State.Position.X.ShouldBeGreaterThan(1f);
    }

    [Fact]
    public void A_teleport_a_respawn_and_a_spawn_set_the_memory_to_zero()
    {
        CharacterSimulation simulation = OnAFloor();

        Remember(simulation);
        simulation.Teleport(new Vector3(1f, 0.05f, 1f));
        simulation.State.Memory.ShouldBe(default);

        Remember(simulation);
        simulation.Respawn();
        simulation.State.Memory.ShouldBe(default);

        Remember(simulation);
        simulation.Spawn();
        simulation.State.Memory.ShouldBe(default);
    }

    private static void Remember(CharacterSimulation simulation)
    {
        CharacterState state = simulation.State;
        state.Memory[2] = 9f;
        simulation.Restore(in state);
        simulation.State.Memory[2].ShouldBe(9f);
    }

    private static CharacterSimulation OnAFloor()
    {
        var scene = new Scene("Memory");
        SceneNode floor = scene.Root.CreateChild("floor");
        floor.LocalPosition = new Vector3(0f, -0.5f, 0f);
        floor.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(
            new Vector3(-8f, -0.5f, -8f), new Vector3(8f, 0.5f, 8f), MaterialRef.Default);
        scene.RebuildStaticWorld(new FakeRenderer());

        var simulation = new CharacterSimulation(scene) { SpawnPosition = new Vector3(0f, 0.05f, 0f) };
        simulation.Spawn();
        for (int i = 0; i < 20; i++)
            simulation.Tick(default, Dt);

        return simulation;
    }
}
