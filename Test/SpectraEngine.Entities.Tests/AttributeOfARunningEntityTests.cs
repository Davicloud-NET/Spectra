using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// What a script's GetAttribute reads against what a built-in entity runs
/// with, where a level names a keyvalue twice or with text its type cannot
/// read.
/// </summary>
public sealed class AttributeOfARunningEntityTests
{
    [Theory]
    [InlineData("1", "3")]
    [InlineData("3", "soon")]
    [InlineData("soon", "3")]
    [InlineData("soon", "later")]
    public void A_keyvalue_written_twice_reads_as_the_door_runs_with_it(string first, string second)
    {
        var scene = new Scene("Door");
        SceneNode node = Movers.Part(scene.Root, "door", new Vector3(1f, 2f, 0.2f), Vector3.Zero);
        node.Entity = new EntityData("func_door");
        node.Entity.Keyvalues.Add(new("wait", first));
        node.Entity.Keyvalues.Add(new("wait", second));
        EntityCatalog classes = EntityRuntime.Catalog([]);
        var world = new EntityWorld(scene, new CapturingLogger(), classes);
        world.Activate();

        AuthoredAttributes.TryRead(node, classes, "wait", out _, out ParameterValue read).ShouldBeTrue();

        read.ShouldBe(ParameterValue.Of((double)EntityRuntime.Live<FuncDoor>(world, node).Wait));
    }
}
