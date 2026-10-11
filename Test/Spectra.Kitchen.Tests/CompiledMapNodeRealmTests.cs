using System.Linq;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// A node's declared realm and state through a real bake and a real load: the
/// cooked level declares what the authored one does.
/// </summary>
public sealed class CompiledMapNodeRealmTests
{
    // Bare nodes after the room, so the chunks are the room's own.
    private static SpectraEngine.Core.Scene.Scene Declared()
    {
        SpectraEngine.Core.Scene.Scene scene = MapFixture.Fresh().BuildScene();

        SceneNode vault = scene.Root.CreateChild("Vault");
        vault.DeclaredRealm = NodeRealm.Server;
        vault.CreateChild("EnemyTemplate").DeclaredState = NodeState.Dormant;

        SceneNode hud = scene.Root.CreateChild("Hud");
        hud.DeclaredRealm = NodeRealm.Client;
        hud.DeclaredState = NodeState.Active;

        scene.Root.CreateChild("Lobby").DeclaredRealm = NodeRealm.Shared;
        return scene;
    }

    private static SceneNode Find(SpectraEngine.Core.Scene.Scene scene, string name) =>
        scene.Root.Traverse().Single(node => node.Name == name);

    [Fact]
    public void Every_node_arrives_declaring_the_realm_and_state_it_was_authored_with()
    {
        using CookedLevel level = CookedLevel.Bake(Declared());

        foreach (SceneNode authored in level.Authored.Root.Traverse())
        {
            if (ReferenceEquals(authored, level.Authored.Root)) continue;

            level.Scene.TryFindById(authored.Id, out SceneNode? node).ShouldBeTrue(authored.Name);
            node.ShouldNotBeNull().DeclaredRealm.ShouldBe(authored.DeclaredRealm, authored.Name);
            node.DeclaredState.ShouldBe(authored.DeclaredState, authored.Name);
        }

        // The comparison saw every value, not a level of defaults.
        Find(level.Scene, "Vault").DeclaredRealm.ShouldBe(NodeRealm.Server);
        Find(level.Scene, "Hud").DeclaredRealm.ShouldBe(NodeRealm.Client);
        Find(level.Scene, "Hud").DeclaredState.ShouldBe(NodeState.Active);
        Find(level.Scene, "Lobby").DeclaredRealm.ShouldBe(NodeRealm.Shared);
        Find(level.Scene, "Floor").DeclaredRealm.ShouldBe(NodeRealm.Inherit);
        Find(level.Scene, "Floor").DeclaredState.ShouldBe(NodeState.Inherit);
    }

    [Fact]
    public void A_node_arrives_with_its_own_declaration_and_not_its_parents()
    {
        using CookedLevel level = CookedLevel.Bake(Declared());

        SceneNode template = Find(level.Scene, "EnemyTemplate");

        template.Parent.ShouldNotBeNull().DeclaredRealm.ShouldBe(NodeRealm.Server);
        template.DeclaredRealm.ShouldBe(NodeRealm.Inherit);
        template.DeclaredState.ShouldBe(NodeState.Dormant);
    }

    [Fact]
    public void A_baked_node_record_holds_the_two_in_bits_3_to_6()
    {
        using CookedLevel level = CookedLevel.Bake(Declared());
        ScmapProbe probe = ScmapProbe.Read(level.File);

        ScmapNodeRecord hud = probe.Nodes[probe.NodeNames.IndexOf("Hud")];
        hud.DeclaredRealm.ShouldBe(ScmapNodeRealm.Client);
        hud.DeclaredState.ShouldBe(ScmapNodeState.Active);
        (hud.PayloadFlagsRaw & 0x78).ShouldBe((3 << 3) | (1 << 5));

        // A node that declares nothing writes no bit there.
        (probe.Nodes[probe.NodeNames.IndexOf("Floor")].PayloadFlagsRaw & 0x78).ShouldBe(0);
    }

    [Fact]
    public void A_state_that_is_none_loads_as_no_declaration_and_is_reported()
    {
        using CookedLevel level = CookedLevel.Bake(Declared());
        byte[] file = (byte[])level.File.Clone();

        // Both bits of the state field: the one value that names no state.
        int hud = ScmapProbe.Read(file).NodeNames.IndexOf("Hud");
        (int nodes, _) = ScmapSurgery.Section(file, ScmapFormat.NodeSection);
        file[nodes + ScmapFormat.NodePreambleSize + (hud * ScmapFormat.NodeRecordSize) + 0x42] |= 0x60;

        var scene = new SpectraEngine.Core.Scene.Scene("empty");
        var renderer = new FakeRenderer();
        CompiledMapLoadReport report =
            CompiledMapLoader.Load(scene, renderer, ContentBlob.CopyOf(file), "Maps/Room.scmap");

        try
        {
            Find(scene, "Hud").DeclaredState.ShouldBe(NodeState.Inherit);
            Find(scene, "Hud").DeclaredRealm.ShouldBe(NodeRealm.Client);
            Find(scene, "EnemyTemplate").DeclaredState.ShouldBe(NodeState.Dormant);
            report.NodesWithInvalidState.ShouldBe(["Hud"]);
            report.IsComplete.ShouldBeFalse();
            report.Describe().ShouldNotBeNull().ShouldContain("Hud");
        }
        finally
        {
            scene.ReleaseCompiledStaticWorld(renderer);
        }
    }
}
