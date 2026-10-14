using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a node's few attributes hold in managed memory: what scripts set,
/// and what the level was built with. The bounds are under what a value of
/// 72 bytes in a dictionary held. <see cref="AttributeBytesCostTests"/>
/// writes the numbers down.
/// </summary>
[Collection(ManagedHeapCollection.Name)]
public sealed class AttributeBytesTests
{
    private const int Nodes = 4000;

    [Theory]
    [InlineData(1, 160)]
    [InlineData(2, 240)]
    [InlineData(4, 340)]
    [InlineData(16, 950)]
    public void Numbers_a_script_sets_on_a_node_hold_few_bytes(int count, int most)
    {
        TableBytes(count, index => MemberValue.Of(index + 0.5)).ShouldBeLessThanOrEqualTo(most);
    }

    [Fact]
    public void A_short_text_a_script_sets_holds_no_more_than_a_number()
    {
        byte[] word = "0123456789abcdef"u8.ToArray();

        TableBytes(1, _ => MemberValue.OfUtf8(word)).ShouldBeLessThanOrEqualTo(160);
    }

    [Fact]
    public void A_long_text_a_script_sets_holds_its_bytes_once()
    {
        byte[] text = new byte[1000];

        TableBytes(1, _ => MemberValue.OfUtf8(text)).ShouldBeLessThanOrEqualTo(1200);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 150)]
    [InlineData(4, 250)]
    [InlineData(16, 850)]
    public void Numbers_a_node_is_built_with_hold_few_bytes(int count, int most)
    {
        BuiltBytes(count, index => MemberValue.Of(index + 0.5)).ShouldBeLessThanOrEqualTo(most);
    }

    [Fact]
    public void A_short_text_a_node_is_built_with_holds_no_more_than_a_number()
    {
        BuiltBytes(1, _ => MemberValue.Of(new string('x', 16))).ShouldBeLessThanOrEqualTo(100);
    }

    private static double TableBytes(int count, Func<int, MemberValue> valueOf)
    {
        var table = new AttributeTable();
        SceneNode[] nodes = [.. Enumerable.Range(0, Nodes).Select(_ => new SceneNode("Part"))];
        string[] names = [.. Enumerable.Range(0, count).Select(index => table.NameOf($"A{index}"))];
        MemberValue[] values = [.. Enumerable.Range(0, count).Select(valueOf)];

        long before = GC.GetTotalMemory(true);
        foreach (SceneNode node in nodes)
        {
            for (int index = 0; index < count; index++)
                table.Set(node, names[index], in values[index]);
        }

        long after = GC.GetTotalMemory(true);
        GC.KeepAlive(table);
        GC.KeepAlive(nodes);
        return (after - before) / (double)Nodes;
    }

    private static double BuiltBytes(int count, Func<int, MemberValue> valueOf)
    {
        var sets = new AttributeSet[Nodes];
        var pairs = new KeyValuePair<string, MemberValue>[count];
        string[] names = [.. Enumerable.Range(0, count).Select(index => $"A{index}")];

        long before = GC.GetTotalMemory(true);
        for (int node = 0; node < Nodes; node++)
        {
            for (int index = 0; index < count; index++)
                pairs[index] = new KeyValuePair<string, MemberValue>(names[index], valueOf(index));

            AttributeSet.TryMake(pairs, out AttributeSet? made, out _).ShouldBeTrue();
            sets[node] = made;
        }

        long after = GC.GetTotalMemory(true);
        GC.KeepAlive(sets);
        return (after - before) / (double)Nodes;
    }
}
