using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What the seed of a play session keeps of a node once it has read the tags
/// and attributes the node was built with: that it was read, and no more.
/// </summary>
public sealed class LevelSeedTests
{
    [Fact]
    public void A_node_is_read_once_so_what_a_script_took_off_stays_off()
    {
        NativeLuau.Require();
        using var host = new LuauHost(new RecordingSink());
        var tags = new TagTable();
        var attributes = new AttributeTable();
        LevelSeed seed = Seed(host, tags, attributes);
        var node = new SceneNode("Marker") { Tags = TagSet.Of("Lava") };

        seed.Read(node, isInLevel: false);
        tags.Remove(node, "Lava", out _);
        seed.Read(node, isInLevel: false);

        tags.Has(node, "Lava").ShouldBeFalse();
    }

    // Whatever takes a node out of the two tables has let go of it. The
    // seed's own note that it was read may not be a third hold on it.
    [Fact]
    public void A_node_the_tables_have_let_go_of_is_not_kept_by_having_been_read()
    {
        NativeLuau.Require();
        using var host = new LuauHost(new RecordingSink());
        var tags = new TagTable();
        var attributes = new AttributeTable();
        LevelSeed seed = Seed(host, tags, attributes);

        WeakReference marker = ReadAMarkedNode(seed, tags, attributes);
        tags.Clear();
        attributes.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        marker.IsAlive.ShouldBeFalse();
        GC.KeepAlive(seed);
    }

    // Apart, so no local of the test keeps the node.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReadAMarkedNode(LevelSeed seed, TagTable tags, AttributeTable attributes)
    {
        var marker = new SceneNode("Marker")
        {
            Tags = TagSet.Of("Lava"),
            Attributes = AttributeSet.Empty.With("Damage", MemberValue.Of(25d)),
        };

        seed.Read(marker, isInLevel: false);

        tags.Has(marker, "Lava").ShouldBeTrue();
        attributes.TryGet(marker, "Damage", out _).ShouldBeTrue();
        return new WeakReference(marker);
    }

    private static LevelSeed Seed(LuauHost host, TagTable tags, AttributeTable attributes) =>
        new(host, new EntityWorld(new Scene("Level"), NullLogger.Instance, new EntityCatalog()), tags, attributes);
}
