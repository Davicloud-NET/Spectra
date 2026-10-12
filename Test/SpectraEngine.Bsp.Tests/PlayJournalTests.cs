using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The journal a running level records in: the first value of each kind on
/// each node, and the order they go back in.
/// </summary>
public sealed class PlayJournalTests
{
    [Fact]
    public void Only_the_first_record_of_a_kind_on_a_node_keeps_a_value()
    {
        var journal = new PlayJournal();
        var node = new SceneNode("Door") { LocalPosition = new Vector3(1f, 2f, 3f) };

        journal.Record(node, JournalKind.LocalTransform);
        node.LocalPosition = new Vector3(4f, 5f, 6f);
        journal.Record(node, JournalKind.LocalTransform);
        node.LocalPosition = new Vector3(7f, 8f, 9f);

        journal.Count.ShouldBe(1);

        journal.Restore();

        node.LocalPosition.ShouldBe(new Vector3(1f, 2f, 3f));
        journal.Count.ShouldBe(0);
    }

    [Fact]
    public void Two_kinds_on_one_node_are_two_entries()
    {
        var journal = new PlayJournal();
        var door = new SceneNode("Door");

        journal.Record(door, JournalKind.LocalTransform);
        journal.Record(door, JournalKind.Name);

        journal.Count.ShouldBe(2);
        journal.CountOf(JournalKind.LocalTransform).ShouldBe(1);
        journal.CountOf(JournalKind.Name).ShouldBe(1);
    }

    [Fact]
    public void One_kind_on_two_nodes_is_two_entries()
    {
        var journal = new PlayJournal();

        // Two nodes that hold the same values are still two.
        journal.Record(new SceneNode("Door"), JournalKind.Name);
        journal.Record(new SceneNode("Door"), JournalKind.Name);

        journal.CountOf(JournalKind.Name).ShouldBe(2);
    }

    [Fact]
    public void Values_go_back_newest_first()
    {
        var journal = new PlayJournal();
        var order = new List<string>();
        var first = new JournalKind<string>(static node => node.Name, (node, _) => order.Add($"first {node.Name}"));
        var second = new JournalKind<string>(static node => node.Name, (node, _) => order.Add($"second {node.Name}"));
        var door = new SceneNode("Door");
        var gate = new SceneNode("Gate");

        journal.Record(door, first);
        journal.Record(gate, first);
        journal.Record(door, second);
        journal.Record(gate, first);

        journal.Restore();

        order.ShouldBe(["second Door", "first Gate", "first Door"]);
    }

    [Fact]
    public void A_put_back_that_throws_does_not_keep_the_older_values_from_going_back()
    {
        var journal = new PlayJournal();
        var faulty = new JournalKind<string>(
            static node => node.Name, static (_, _) => throw new InvalidOperationException("A handler threw."));
        var door = new SceneNode("Door") { LocalPosition = new Vector3(1f, 2f, 3f) };

        journal.Record(door, JournalKind.LocalTransform);
        door.LocalPosition = new Vector3(4f, 5f, 6f);
        journal.Record(door, JournalKind.Name);
        door.Name = "Gate";
        journal.Record(new SceneNode("Latch"), faulty);

        Should.Throw<InvalidOperationException>(journal.Restore).Message.ShouldBe("A handler threw.");

        door.LocalPosition.ShouldBe(new Vector3(1f, 2f, 3f));
        door.Name.ShouldBe("Door");
        journal.Count.ShouldBe(0);
    }

    [Fact]
    public void A_journal_that_was_restored_records_a_node_again()
    {
        var journal = new PlayJournal();
        var node = new SceneNode("Door");

        journal.Record(node, JournalKind.LocalTransform);
        node.LocalPosition = Vector3.One;
        journal.Restore();

        node.LocalPosition = new Vector3(2f, 0f, 0f);
        journal.Record(node, JournalKind.LocalTransform);
        node.LocalPosition = new Vector3(3f, 0f, 0f);
        journal.Restore();

        node.LocalPosition.ShouldBe(new Vector3(2f, 0f, 0f));
    }

    [Fact]
    public void A_record_of_a_node_that_is_recorded_allocates_nothing()
    {
        var journal = new PlayJournal();
        var node = new SceneNode("Door");
        journal.Record(node, JournalKind.LocalTransform);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            journal.Record(node, JournalKind.LocalTransform);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
        journal.Count.ShouldBe(1);
    }

    [Fact]
    public void What_is_no_node_records_its_first_value_and_gets_it_back_among_the_nodes()
    {
        var journal = new PlayJournal();
        var tuning = new CharacterTuning { WalkSpeed = 6f };
        var other = new CharacterTuning();
        var door = new SceneNode("Door");

        journal.Record(tuning, TuningJournalKind.WalkSpeed);
        tuning.WalkSpeed = 9f;
        journal.Record(door, JournalKind.Name);
        door.Name = "Gate";
        journal.Record(tuning, TuningJournalKind.WalkSpeed);
        tuning.WalkSpeed = 12f;
        journal.Record(other, TuningJournalKind.JumpHeight);
        other.JumpHeight = 3f;

        journal.Count.ShouldBe(3);
        journal.CountOf(TuningJournalKind.WalkSpeed).ShouldBe(1);

        journal.Restore();

        tuning.WalkSpeed.ShouldBe(6f);
        other.JumpHeight.ShouldBe(new CharacterTuning().JumpHeight);
        door.Name.ShouldBe("Door");
    }
}
