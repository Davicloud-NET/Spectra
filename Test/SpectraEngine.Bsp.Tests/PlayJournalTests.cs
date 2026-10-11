using System.Numerics;
using SpectraEngine.Core.Entities;
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
    public void Two_kinds_on_one_node_and_one_kind_on_two_nodes_are_entries_of_their_own()
    {
        var journal = new PlayJournal();
        var door = new SceneNode("Door");
        var gate = new SceneNode("Gate");
        var mark = new JournalKind<string>(static node => node.Name, static (node, first) => node.Name = first);

        journal.Record(door, JournalKind.LocalTransform);
        journal.Record(door, mark);
        journal.Record(gate, mark);
        journal.Record(gate, mark);

        journal.Count.ShouldBe(3);
        journal.CountOf(JournalKind.LocalTransform).ShouldBe(1);
        journal.CountOf(mark).ShouldBe(2);
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
}
