using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What the level's tags tell the rest of the runtime when a script gives a
/// node in no level a tag.
/// </summary>
public sealed class LevelTagsTests
{
    // Asking for room can let go of loose trees and their tags. What the new
    // tag added is taken before that.
    [Fact]
    public void A_tag_on_a_node_in_no_level_is_told_as_grown_by_its_own_weight()
    {
        NativeLuau.Require();
        using var host = new LuauHost(new RecordingSink());
        var table = new TagTable();
        var spare = new SceneNode("Spare");
        table.Add(spare, "Spare", isInLevel: false);
        var world = new EntityWorld(new Scene("Level"), NullLogger.Instance, new EntityCatalog());
        long told = -1;
        var tags = new LevelTags(host, world, new SignalHub(host, new WaitingThreads()), new DeferredCalls(world), table)
        {
            HasRoom = () =>
            {
                table.Forget(spare);
                return true;
            },
            ChangedOutside = (_, grown) => told = grown,
        };

        tags.Add(new SceneNode("Loose"), "Fresh").ShouldBeTrue();

        told.ShouldBe(NodeWeight.OfTag + (2 * "Fresh".Length));
    }
}
