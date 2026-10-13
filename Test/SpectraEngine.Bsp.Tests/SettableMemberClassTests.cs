using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The classes a settable member names against the nodes it reads a value
/// on. The script API declares a member on those classes, so the two have
/// to say the same.
/// </summary>
public sealed class SettableMemberClassTests
{
    [Fact]
    public void A_member_reads_a_value_on_a_node_of_its_classes_and_on_no_other()
    {
        foreach (SceneNode node in OneOfEveryClass())
        {
            foreach (SettableMember member in ScriptWrites.Settable)
            {
                bool isOfAClass = member.Classes.Any(named => NodeClass.IsA(node, named));
                string asked = $"{member.Name} of a {NodeClass.NameOf(NodeClass.KindOf(node))}";

                ScriptWrites.Has(node, member.Id).ShouldBe(isOfAClass, asked);
                (ScriptWrites.Read(node, member.Id).Kind == member.Kind).ShouldBe(isOfAClass, asked);
            }
        }
    }

    [Fact]
    public void A_member_names_built_in_classes_only_and_every_node_has_the_ones_that_name_Instance()
    {
        foreach (SettableMember member in ScriptWrites.Settable)
        {
            member.Classes.ShouldNotBeEmpty(member.Name);
            foreach (string named in member.Classes)
                (named == NodeClass.Instance || NodeClass.BaseOf(named) is not null).ShouldBeTrue($"{member.Name}: {named}");
        }

        default(SettableMember).Classes.ShouldBe(SettableMember.EveryNode);
    }

    private static List<SceneNode> OneOfEveryClass()
    {
        var scene = new Scene("Classes");
        SceneNode root = scene.Root;
        List<SceneNode> nodes =
        [
            root.CreateChild("Folder"),
            EntityMotion.BrushNode(root, "Part", BrushKind.Part, Vector3.Zero),
            EntityMotion.BrushNode(root, "Wall", BrushKind.World, Vector3.Zero),
            SpatialTestHelpers.CreateMeshNode(root, "Mesh", Vector3.Zero),
            new SceneNode("BakedWall") { BakedKind = SceneNodeKind.BrushWorld },
            new SceneNode("BakedMesh") { BakedKind = SceneNodeKind.Mesh },
            new SceneNode("Script") { Script = new Script("Scripts/a.luau") },
            new SceneNode("Module") { Script = new Script("Scripts/b.luau") { IsModule = true } },
            new SceneNode("Relay") { Entity = new EntityData("logic_relay") },
        ];

        foreach (LightKind kind in Enum.GetValues<LightKind>())
            nodes.Add(new SceneNode(kind.ToString()) { Light = new Light { Kind = kind } });

        Enum.GetValues<NodeClassKind>().ShouldBeSubsetOf(nodes.Select(NodeClass.KindOf));
        return nodes;
    }
}
