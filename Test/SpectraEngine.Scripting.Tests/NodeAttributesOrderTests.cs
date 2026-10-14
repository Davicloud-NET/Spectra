using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The names of a node's attributes come out in the order a dictionary hands
/// them out, through removals and past the switch to a dictionary, since
/// GetAttributes fills its table in that order.
/// </summary>
public sealed class NodeAttributesOrderTests
{
    [Fact]
    public void A_new_name_takes_the_slot_let_go_last()
    {
        var pair = new Pair();
        pair.Set("A", 1);
        pair.Set("B", 2);
        pair.Set("C", 3);
        pair.Set("D", 4);
        pair.Remove("B");
        pair.Remove("D");
        pair.Set("E", 5);
        pair.Set("F", 6);
        pair.Set("G", 7);

        pair.ShouldAgree();
        pair.Names().ShouldBe(["A", "F", "C", "E", "G"]);
    }

    [Fact]
    public void Taking_the_first_name_away_frees_the_slot_on_the_node()
    {
        var pair = new Pair();
        pair.Set("A", 1);
        pair.Set("B", 2);
        pair.Remove("A");
        pair.Set("C", 3);

        pair.ShouldAgree();
        pair.Names().ShouldBe(["C", "B"]);
    }

    [Fact]
    public void Names_past_the_walked_ones_keep_the_order_after_removals()
    {
        var pair = new Pair();
        for (int i = 0; i < NodeAttributes.MostWalked; i++)
            pair.Set("N" + i, i);

        pair.Remove("N3");
        pair.Remove("N17");
        pair.Remove("N0");
        for (int i = 0; i < 6; i++)
            pair.Set("M" + i, 100 + i);

        pair.Remove("M4");
        pair.Remove("N20");
        pair.Set("L0", 200);
        pair.Set("L1", 201);

        pair.ShouldAgree();
    }

    [Fact]
    public void Any_run_of_sets_and_removals_reads_as_a_dictionary_does()
    {
        var random = new Random(7);
        var pair = new Pair();
        for (int step = 0; step < 4000; step++)
        {
            string name = "K" + random.Next(48);
            if (random.Next(3) == 0)
                pair.Remove(name);
            else
                pair.Set(name, step);

            pair.ShouldAgree();
        }
    }

    // A NodeAttributes and a dictionary given the same calls.
    private sealed class Pair
    {
        private readonly NodeAttributes _attributes = new();
        private readonly Dictionary<string, MemberValue> _dictionary = new(StringComparer.Ordinal);

        public void Set(string name, double number)
        {
            _attributes.Set(name, MemberValue.Of(number));
            _dictionary[name] = MemberValue.Of(number);
        }

        public void Remove(string name)
        {
            _attributes.Remove(name).ShouldBe(_dictionary.Remove(name));
        }

        public List<string> Names()
        {
            var names = new List<string>();
            foreach (KeyValuePair<string, MemberValue> pair in _attributes)
                names.Add(pair.Key);

            return names;
        }

        public void ShouldAgree()
        {
            var held = new List<KeyValuePair<string, MemberValue>>();
            foreach (KeyValuePair<string, MemberValue> pair in _attributes)
                held.Add(pair);

            held.ShouldBe(_dictionary.ToList());
            _attributes.Count.ShouldBe(_dictionary.Count);
        }
    }
}
