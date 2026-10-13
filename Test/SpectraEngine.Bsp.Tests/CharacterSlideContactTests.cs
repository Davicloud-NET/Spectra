using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a tick of the mover says its slide stopped at: the parts, by id, and
/// only for the tick that ran into them.
/// </summary>
public sealed class CharacterSlideContactTests
{
    private const float Dt = 1f / 60f;

    private static readonly CharacterCommand Ahead = new() { MoveForward = CharacterCommand.Axis(1f) };

    [Fact]
    public void A_walk_into_a_part_names_it_on_every_tick_that_pushes_against_it_and_on_no_other()
    {
        var scene = new Scene("Walk");
        Part(scene, "floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 0.5f, 5f));
        SceneNode wall = Part(scene, "wall", new Vector3(3f, 1f, 0f), new Vector3(0.5f, 1f, 2f));
        CharacterSimulation character = Spawned(scene);

        // On the floor and on its way: the floor is ground, not a stop.
        for (int tick = 0; tick < 10; tick++)
            character.Tick(in Ahead, Dt);
        character.State.Grounded.ShouldBeTrue();
        character.State.SlideContacts.Count.ShouldBe(0);

        for (int tick = 0; tick < 120; tick++)
            character.Tick(in Ahead, Dt);
        character.State.Position.X.ShouldBe(2.5f - character.Tuning.Radius - character.Tuning.SkinWidth, 0.01f);
        character.State.SlideContacts.Count.ShouldBe(1);
        character.State.SlideContacts[0].ShouldBe(wall.Id);

        // At rest against it, nothing slides.
        for (int tick = 0; tick < 30; tick++)
            character.Tick(default, Dt);
        character.State.SlideContacts.Count.ShouldBe(0);
    }

    [Fact]
    public void A_walk_into_a_world_brush_names_nothing()
    {
        var scene = new Scene("Walk");
        SpatialTestHelpers.CreateBrushNode(scene.Root, "ground", new Vector3(0f, -10f, 0f), half: 10f);
        SpatialTestHelpers.CreateBrushNode(scene.Root, "wall", new Vector3(3f, 1f, 0f), half: 1f);
        scene.RebuildStaticWorld(new FakeRenderer());
        CharacterSimulation character = Spawned(scene);

        for (int tick = 0; tick < 120; tick++)
        {
            character.Tick(in Ahead, Dt);
            character.State.SlideContacts.Count.ShouldBe(0, $"tick {tick}");
        }

        character.State.Position.X.ShouldBe(2f - character.Tuning.Radius - character.Tuning.SkinWidth, 0.01f);
    }

    [Fact]
    public void A_teleport_forgets_what_the_last_tick_ran_into()
    {
        var scene = new Scene("Walk");
        Part(scene, "floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 0.5f, 5f));
        Part(scene, "wall", new Vector3(3f, 1f, 0f), new Vector3(0.5f, 1f, 2f));
        CharacterSimulation character = Spawned(scene);
        for (int tick = 0; tick < 120; tick++)
            character.Tick(in Ahead, Dt);
        character.State.SlideContacts.Count.ShouldBe(1);

        character.Teleport(new Vector3(-5f, 0.05f, 0f));

        character.State.SlideContacts.Count.ShouldBe(0);
    }

    [Fact]
    public void A_replaced_mover_that_names_nothing_leaves_nothing_named()
    {
        var scene = new Scene("Walk");
        Part(scene, "floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 0.5f, 5f));
        SceneNode wall = Part(scene, "wall", new Vector3(3f, 1f, 0f), new Vector3(0.5f, 1f, 2f));
        CharacterSimulation character = Spawned(scene);
        CharacterState state = character.State;
        state.SlideContacts.Add(wall.Id);
        character.Restore(in state);

        character.Mover = new StillMover();
        character.Tick(in Ahead, Dt);

        character.State.SlideContacts.Count.ShouldBe(0);
    }

    [Fact]
    public void The_list_keeps_the_first_four_and_takes_no_node_twice_and_no_empty_id()
    {
        Guid[] nodes = [.. Enumerable.Range(0, 6).Select(_ => Guid.NewGuid())];
        var contacts = default(CharacterSlideContacts);

        contacts.Add(Guid.Empty);
        contacts.Add(nodes[0]);
        contacts.Add(nodes[0]);
        contacts.Count.ShouldBe(1);

        foreach (Guid node in nodes)
            contacts.Add(node);

        contacts.Count.ShouldBe(CharacterSlideContacts.Capacity);
        for (int i = 0; i < contacts.Count; i++)
            contacts[i].ShouldBe(nodes[i]);
        contacts.Contains(nodes[3]).ShouldBeTrue();
        contacts.Contains(nodes[4]).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => contacts[4]);
    }

    // A test compares two states as wholes, and a replay will.
    [Fact]
    public void Two_states_are_equal_when_they_name_the_same_parts_and_not_when_they_differ()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        CharacterState one = CharacterState.AtFeet(Vector3.One);
        CharacterState other = CharacterState.AtFeet(Vector3.One);
        one.ShouldBe(other);

        one.SlideContacts.Add(first);
        one.ShouldNotBe(other);

        other.SlideContacts.Add(first);
        one.ShouldBe(other);
        one.SlideContacts.GetHashCode().ShouldBe(other.SlideContacts.GetHashCode());

        one.SlideContacts.Add(second);
        other.SlideContacts.Add(Guid.NewGuid());
        one.ShouldNotBe(other);
        (one.SlideContacts == other.SlideContacts).ShouldBeFalse();
    }

    private static CharacterSimulation Spawned(Scene scene)
    {
        var character = new CharacterSimulation(scene) { SpawnPosition = new Vector3(0f, 0.05f, 0f) };
        character.Spawn();
        return character;
    }

    private static SceneNode Part(Scene scene, string name, Vector3 center, Vector3 half)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = center;

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(-half, half);
        return node;
    }

    // Moves nobody and writes nothing of its own.
    private sealed class StillMover : ICharacterMover
    {
        public void Tick(
            ref CharacterState state,
            in CharacterCommand command,
            ICharacterCollisionSource source,
            CharacterTuning tuning,
            float deltaTime)
        {
        }
    }
}
