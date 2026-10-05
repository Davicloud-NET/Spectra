using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Rules;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// Entities and node flags through a real bake and a real load: the cooked
/// level carries what the authored one does.
/// </summary>
public class CompiledMapEntityLoadTests
{
    // The door, its trigger, the relay and the player start.
    private const int FixtureEntities = 4;

    [Fact]
    public void Every_entity_arrives_on_its_node_with_keyvalues_and_wires_in_authored_order()
    {
        using Cooked level = Cooked.Bake();

        int entities = 0;
        foreach (SceneNode authored in Nodes(level.Authored))
        {
            SceneNode node = Counterpart(level, authored);

            if (authored.Entity is not { } expected)
            {
                node.Entity.ShouldBeNull(authored.Name);
                continue;
            }

            entities++;
            EntityData actual = node.Entity.ShouldNotBeNull(authored.Name);
            actual.ClassName.ShouldBe(expected.ClassName);
            actual.Keyvalues.ShouldBe(expected.Keyvalues);
            actual.Connections.ShouldBe(expected.Connections);
        }

        entities.ShouldBe(FixtureEntities);
        level.Report.EntitiesLoaded.ShouldBe(FixtureEntities);

        // A repeated key stays two entries, in place.
        EntityData relay = Find(level.Scene, "Relay").Entity.ShouldNotBeNull();
        relay.Keyvalues.ShouldBe([new("tag", "first"), new("spawnflags", "1"), new("tag", "second")]);

        // A delay and a fire limit survive as numbers.
        relay.Connections.Single().TimesToFire.ShouldBe(1);
        Find(level.Scene, "DoorTrigger").Entity.ShouldNotBeNull().Connections[1]
            .ShouldBe(new EntityConnection("OnEndTouch", "Door", "Close", "", 2.5f, EntityConnection.Infinite));
    }

    [Fact]
    public void Every_node_arrives_with_the_flags_it_was_authored_with()
    {
        using Cooked level = Cooked.Bake();

        foreach (SceneNode authored in Nodes(level.Authored))
        {
            SceneNode node = Counterpart(level, authored);

            node.CanCollide.ShouldBe(authored.CanCollide, authored.Name);
            node.CanQuery.ShouldBe(authored.CanQuery, authored.Name);
            node.CanTouch.ShouldBe(authored.CanTouch, authored.Name);
            node.IsRendered.ShouldBe(authored.IsRendered, authored.Name);
        }

        // A trigger volume: touched, never bumped into, never drawn.
        SceneNode trigger = Find(level.Scene, "DoorTrigger");
        trigger.CanCollide.ShouldBeFalse();
        trigger.CanQuery.ShouldBeFalse();
        trigger.CanTouch.ShouldBeTrue();
        trigger.IsRendered.ShouldBeFalse();
        level.Scene.HiddenBrushNodes.ShouldContain(trigger);

        // Each flag has its own bit: this node has only touch off.
        SceneNode start = Find(level.Scene, "Start");
        start.CanTouch.ShouldBeFalse();
        start.CanCollide.ShouldBeTrue();
        start.CanQuery.ShouldBeTrue();
        start.IsRendered.ShouldBeTrue();

        SceneNode door = Find(level.Scene, "Door");
        door.PhysicsFlags.ShouldBe(PhysicsFlags.Default);
        door.IsRendered.ShouldBeTrue();
        level.Scene.HiddenBrushNodes.ShouldNotContain(door);
    }

    [Fact]
    public void An_entity_world_over_the_loaded_scene_builds_the_same_entities_as_over_the_authored_one()
    {
        using Cooked level = Cooked.Bake();

        List<string> authored = DescribeEntityWorld(level.Authored);
        List<string> cooked = DescribeEntityWorld(level.Scene);

        cooked.ShouldBe(authored);

        // The comparison saw real classes, placeholders, parsed keys and wires.
        authored.Count(line => line.StartsWith("entity ", StringComparison.Ordinal)).ShouldBe(FixtureEntities);
        authored.ShouldContain("Door: speed = 100");
        authored.ShouldContain(line => line.Contains(nameof(PlaceholderEntity)));
        authored.ShouldContain(line => line.Contains("OnStartTouch"));
    }

    [Fact]
    public void Loading_a_level_with_entities_runs_no_carve_at_all()
    {
        using Cooked level = Cooked.Bake();

        level.CarvesDuringLoad.ShouldBe(0, "attaching an entity or a flag must not start a compile");
        level.Scene.StaticWorld.ShouldBeNull();
        level.Scene.StaticWorldCompileCount.ShouldBe(0);
        level.Scene.RefusedStaticWorldDirtyMarks.ShouldBe(0);
        level.Report.IsComplete.ShouldBeTrue(level.Report.Describe());
    }

    [Fact]
    public void A_map_from_before_entities_is_refused_rather_than_loaded_without_them()
    {
        // Version 1 wrote no entities. Loaded anyway, it is a level where
        // nothing happens.
        using Cooked level = Cooked.Bake();
        byte[] file = (byte[])level.File.Clone();
        BitConverter.GetBytes((ushort)1).CopyTo(file, 0x04);

        ScmapFormatException refused = Should.Throw<ScmapFormatException>(
            () => CompiledMapLoader.Load(
                new SpectraEngine.Core.Scene.Scene("empty"),
                new FakeRenderer(),
                ContentBlob.CopyOf(file),
                "Maps/Room.scmap"));

        EngineInfo.CompiledMapFormatVersion.ShouldBeGreaterThan((ushort)1);
        refused.Message.ShouldContain("format version 1,");
        refused.Message.ShouldContain($"reads version {EngineInfo.CompiledMapFormatVersion}");
        refused.Message.ShouldContain("Recook");
    }

    // One line per entity, per keyvalue it parsed and per wire it holds, in the
    // world's own order.
    private static List<string> DescribeEntityWorld(SpectraEngine.Core.Scene.Scene scene)
    {
        var lines = new List<string>();

        // The other two classes are left out, so they build as placeholders.
        var catalog = new EntityCatalog();
        catalog.Add(new EntitySchema("func_door"), () => new KeyvalueLogEntity(lines));
        catalog.Add(new EntitySchema("logic_relay"), () => new KeyvalueLogEntity(lines));

        var world = new EntityWorld(scene, new CapturingLogger(), catalog);
        world.Activate();

        foreach (Entity entity in world.Entities)
        {
            lines.Add($"entity {entity.Node.Id} {entity.GetType().Name} {entity.ClassName} '{entity.TargetName}'");

            foreach (EntityOutput output in entity.Outputs)
            {
                for (int i = 0; i < output.WireCount; i++)
                    lines.Add($"  {output.Name}[{i}] {output.ConnectionAt(i)} fires left {output.FiresLeftAt(i)}");
            }
        }

        world.Deactivate();
        return lines;
    }

    private static SceneNode Counterpart(Cooked level, SceneNode authored)
    {
        level.Scene.TryFindById(authored.Id, out SceneNode? node).ShouldBeTrue(authored.Name);
        return node.ShouldNotBeNull();
    }

    private static IEnumerable<SceneNode> Nodes(SpectraEngine.Core.Scene.Scene scene) =>
        scene.Root.Traverse().Where(node => !ReferenceEquals(node, scene.Root));

    private static SceneNode Find(SpectraEngine.Core.Scene.Scene scene, string name) =>
        Nodes(scene).Single(node => node.Name == name);

    // The entity fixture, cooked through the real rule and loaded, beside the
    // same bundle bound as the editor binds it.
    private sealed class Cooked : IDisposable
    {
        public required TempProject Project { get; init; }
        public required byte[] File { get; init; }
        public required SpectraEngine.Core.Scene.Scene Scene { get; init; }
        public required FakeRenderer Renderer { get; init; }
        public required SpectraEngine.Core.Scene.Scene Authored { get; init; }
        public required CompiledMapLoadReport Report { get; init; }
        public required long CarvesDuringLoad { get; init; }

        public static Cooked Bake()
        {
            var project = new TempProject();

            try
            {
                MapFixture.Fresh().WriteBundle(project, "Room.smap", withEntities: true);

                var context = new RuleContext(project.Root, "Maps/Room.smap", CookProfile.Ship);
                new MapRule().Cook(context);

                context.Diagnostics.Count.ShouldBe(
                    0, string.Join(Environment.NewLine, context.Diagnostics.Select(d => d.ToString())));

                byte[] file = context.Emissions[0].Payload;

                var renderer = new FakeRenderer();
                var scene = new SpectraEngine.Core.Scene.Scene("empty");

                long before = Csg.CarveInvocationsOnThisThread;
                CompiledMapLoadReport report =
                    CompiledMapLoader.Load(scene, renderer, ContentBlob.CopyOf(file), "Maps/Room.scmap");
                long carves = Csg.CarveInvocationsOnThisThread - before;

                MapDocument document = MapBundle.Load(Path.Combine(project.Layout.MapsPath, "Room.smap"));
                var authored = new SpectraEngine.Core.Scene.Scene(document.Scene.Name);
                MapSceneBinder.ApplyTo(document, authored);

                return new Cooked
                {
                    Project = project,
                    File = file,
                    Scene = scene,
                    Renderer = renderer,
                    Authored = authored,
                    Report = report,
                    CarvesDuringLoad = carves,
                };
            }
            catch
            {
                project.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Scene.ReleaseCompiledStaticWorld(Renderer);
            Project.Dispose();
        }
    }
}
