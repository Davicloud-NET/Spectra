using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// GetTagged gives a tag's nodes in the order of the tree, whatever order
/// they were tagged in, and is paid by those nodes and not by the level.
/// </summary>
public sealed class TagOrderTests
{
    private const string ListLit = """
        local function list()
            local names = {}
            for _, node in game:GetService("CollectionService"):GetTagged("Lit") do
                table.insert(names, node:GetFullName())
            end
            print(table.concat(names, " "))
        end

        """;

    [Fact]
    public void The_nodes_of_a_tag_come_in_the_order_of_the_tree_whatever_order_they_were_tagged_in()
    {
        string[] printed = House.Ask(ListLit + """
            local house = workspace.House
            house.Sign:AddTag("Lit")
            house.Lamp.Shade:AddTag("Lit")
            workspace.Dish:AddTag("Lit")
            house.Walls.South:AddTag("Lit")
            house:AddTag("Lit")
            house.Lamp:AddTag("Lit")
            workspace:AddTag("Lit")
            house.Walls.North:AddTag("Lit")
            list()
            """);

        printed.ShouldBe(
        [
            "Root Root.House Root.House.Walls.North Root.House.Walls.South Root.House.Lamp " +
            "Root.House.Lamp.Shade Root.House.Sign Root.Dish",
        ]);
    }

    [Fact]
    public void The_order_follows_a_node_that_moves_in_the_tree()
    {
        using var level = new ScriptLevel();
        SceneNode row = level.Scene.Root.CreateChild("Row");
        SceneNode a = level.Part("A", new Vector3(1f, 1f, 3f), row);
        level.Part("B", new Vector3(2f, 1f, 3f), row);
        SceneNode c = level.Part("C", new Vector3(3f, 1f, 3f), row);
        SceneNode shelf = level.Scene.Root.CreateChild("Shelf");
        level.AddScript(level.Scene.Root, ListLit + """
            for _, tile in workspace.Row:GetChildren() do
                tile:AddTag("Lit")
            end
            while true do
                list()
                task.wait()
            end
            """);
        level.Play().Tick();

        // To the end of its own parent, then under another, then back in front.
        row.AddChild(a);
        level.Tick();
        shelf.AddChild(c);
        level.Tick();
        row.InsertChild(0, c);
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "Root.Row.A Root.Row.B Root.Row.C",
            "Root.Row.B Root.Row.C Root.Row.A",
            "Root.Row.B Root.Row.A Root.Shelf.C",
            "Root.Row.C Root.Row.B Root.Row.A",
        ]);
    }

    [Fact]
    public void Nodes_are_put_in_the_order_of_the_tree_a_node_before_what_is_below_it()
    {
        var root = new SceneNode("Root");
        SceneNode a = root.CreateChild("A");
        SceneNode a1 = a.CreateChild("A1");
        SceneNode a11 = a1.CreateChild("A11");
        SceneNode a2 = a.CreateChild("A2");
        SceneNode b = root.CreateChild("B");
        SceneNode b1 = b.CreateChild("B1");
        var order = new TreeOrder();

        foreach (SceneNode[] shuffled in new SceneNode[][]
                 {
                     [b1, a2, a11, root, b, a1, a], [a, a1, a11, a2, b, b1, root], [b1, b, a2, a11, a1, a, root],
                 })
        {
            List<SceneNode> nodes = [.. shuffled];
            order.Sort(nodes);
            nodes.Select(node => node.Name).ShouldBe(["Root", "A", "A1", "A11", "A2", "B", "B1"]);
        }

        List<SceneNode> some = [b1, a11];
        order.Sort(some);
        some.ShouldBe([a11, b1]);

        List<SceneNode> one = [a2];
        order.Sort(one);
        one.ShouldBe([a2]);
    }

    // A tree that is not the level's: what left the level still has an order.
    [Fact]
    public void Nodes_of_two_trees_come_tree_by_tree()
    {
        var first = new SceneNode("First");
        SceneNode f1 = first.CreateChild("F1");
        SceneNode f2 = first.CreateChild("F2");
        var second = new SceneNode("Second");
        SceneNode s1 = second.CreateChild("S1");
        List<SceneNode> nodes = [f2, s1, f1];

        new TreeOrder().Sort(nodes);

        nodes.ShouldBe([f1, f2, s1]);
    }

    // Two thousand nodes beside the two that are tagged. The way up from
    // each is three nodes long, and their paths part at the root, whose
    // children are looked over until both are found. So the two branches
    // cost 8 when they are the root's first children and 28 behind the
    // twenty crowds, and the nodes inside the crowds cost nothing.
    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 28)]
    public void Putting_nodes_in_order_looks_at_what_is_above_them_and_at_the_children_where_their_paths_part(
        bool areFirst, int lookedAt)
    {
        var root = new SceneNode("Root");
        SceneNode? near = areFirst ? root.CreateChild("Near") : null;
        SceneNode? far = areFirst ? root.CreateChild("Far") : null;
        for (int i = 0; i < 20; i++)
        {
            SceneNode crowd = root.CreateChild($"Crowd{i}");
            for (int j = 0; j < 100; j++)
                crowd.CreateChild($"Node{j}");
        }

        SceneNode nearCup = (near ?? root.CreateChild("Near")).CreateChild("Shelf").CreateChild("Cup");
        SceneNode farCup = (far ?? root.CreateChild("Far")).CreateChild("Shelf").CreateChild("Cup");

        var order = new TreeOrder();
        List<SceneNode> nodes = [farCup, nearCup];
        order.Sort(nodes);

        nodes.ShouldBe([nearCup, farCup]);
        order.LookedAt.ShouldBe(lookedAt);
    }

    // The worst case: every part sits under the root, so the root's children
    // are the level, and they are looked over up to the last tagged one.
    [Fact]
    public void In_a_flat_level_putting_nodes_in_order_looks_at_every_node_up_to_the_last_one_tagged()
    {
        var root = new SceneNode("Root");
        for (int i = 0; i < 2000; i++)
            root.CreateChild($"Part{i}");

        SceneNode early = root.Children[10];
        SceneNode late = root.Children[1500];

        var order = new TreeOrder();
        List<SceneNode> nodes = [late, early];
        order.Sort(nodes);

        nodes.ShouldBe([early, late]);
        order.LookedAt.ShouldBe(2 + 1501);
    }

    [Fact]
    public void However_many_nodes_lose_a_tag_its_list_is_tidied_once_when_it_is_asked_for_next()
    {
        var root = new SceneNode("Root");
        var table = new TagTable();
        List<SceneNode> nodes = [];
        for (int i = 0; i < 1000; i++)
        {
            nodes.Add(root.CreateChild($"Part{i}"));
            table.Add(nodes[i], "Lit", isInLevel: true);
        }

        TagNodes lit = table.Find("Lit").ShouldNotBeNull();
        table.InOrder(lit).ShouldBe(nodes);

        for (int i = 0; i < 1000; i += 2)
            table.Remove(nodes[i], "Lit", out _);

        table.Tidyings.ShouldBe(0);
        table.InOrder(lit).ShouldBe(nodes.Where((_, index) => index % 2 == 1));
        table.InOrder(lit).Count.ShouldBe(500);
        table.Tidyings.ShouldBe(1);
        table.Orderings.ShouldBe(1);
    }

    [Fact]
    public void A_node_that_loses_a_tag_and_gets_it_back_before_the_list_is_asked_for_is_in_it_once()
    {
        var root = new SceneNode("Root");
        SceneNode a = root.CreateChild("A");
        SceneNode b = root.CreateChild("B");
        SceneNode c = root.CreateChild("C");
        var table = new TagTable();
        foreach (SceneNode node in new[] { a, b, c })
            table.Add(node, "Lit", isInLevel: true);

        TagNodes lit = table.Find("Lit").ShouldNotBeNull();
        table.InOrder(lit).ShouldBe([a, b, c]);

        table.Remove(b, "Lit", out _);
        table.Add(b, "Lit", isInLevel: true);
        table.Leave(c);
        table.Enter(c);

        table.InOrder(lit).ShouldBe([a, b, c]);
    }

    [Fact]
    public void A_list_that_is_in_order_is_handed_over_as_it_is_until_a_node_joins_it_or_the_tree_moves()
    {
        var root = new SceneNode("Root");
        SceneNode a = root.CreateChild("A");
        SceneNode b = root.CreateChild("B");
        SceneNode c = root.CreateChild("C");
        var table = new TagTable();
        table.Add(c, "Lit", isInLevel: true);
        TagNodes lit = table.Add(a, "Lit", isInLevel: true).ShouldNotBeNull();

        table.InOrder(lit).ShouldBe([a, c]);
        table.Orderings.ShouldBe(1);

        // Asked again, after a node left the list and after another tag
        // got a node: the list is as it was.
        table.InOrder(lit).ShouldBe([a, c]);
        table.Remove(a, "Lit", out bool wasInLevel).ShouldBe(lit);
        wasInLevel.ShouldBeTrue();
        table.Add(b, "Other", isInLevel: true);
        table.InOrder(lit).ShouldBe([c]);
        table.Orderings.ShouldBe(1);

        // A node that is not in the level joins no list.
        table.Add(b, "Lit", isInLevel: true);
        table.Add(a, "Lit", isInLevel: false);
        table.InOrder(lit).ShouldBe([b, c]);
        table.InOrder(lit).ShouldBe([b, c]);
        table.Orderings.ShouldBe(2);

        root.InsertChild(0, c);
        table.TreeMoved();
        table.InOrder(lit).ShouldBe([c, b]);
        table.Orderings.ShouldBe(3);
    }
}
