using System;
using System.Numerics;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

public sealed class CollisionPublicationTests
{
    [Fact]
    public void Remote_publications_skip_selection_but_nearby_edits_and_overflow_reselect()
    {
        var scene = new Scene();
        var renderer = new FakeRenderer();
        var local = Box(scene, "local", Vector3.Zero, new Vector3(2));
        var remote = Box(scene, "remote", new Vector3(1000, 0, 0), new Vector3(2));
        scene.RebuildStaticWorld(renderer);
        var source = new BrushPlaneCollisionSource(scene, new CharacterTuning());
        var volume = new Aabb(new Vector3(-1), new Vector3(1));
        source.BeginTick(volume, default);
        int selections = source.WorldSelections;
        int revision = source.Revision;
        PublishMove(remote, scene, renderer);
        PublishMove(remote, scene, renderer);
        source.BeginTick(volume, default);
        source.WorldSelections.ShouldBe(selections);
        source.Revision.ShouldBe(revision);

        // A nearby edit must not get lost behind a later remote publication.
        PublishMove(local, scene, renderer);
        PublishMove(remote, scene, renderer);
        source.BeginTick(volume, default);
        source.WorldSelections.ShouldBe(++selections);
        source.Revision.ShouldBeGreaterThan(revision);

        for (int i = 0; i < 257; i++) PublishMove(remote, scene, renderer);
        source.BeginTick(volume, default);
        source.WorldSelections.ShouldBe(++selections);
        source.BeginTick(new Aabb(new Vector3(999, -1, -1), new Vector3(1001, 1, 1)), default);
        source.WorldSelections.ShouldBe(++selections);
    }

    [Fact]
    public void Cutters_outside_the_character_region_remain_dependencies_of_selected_additives()
    {
        var scene = new Scene();
        var renderer = new FakeRenderer();
        Box(scene, "long floor", Vector3.Zero, new Vector3(200, 1, 2));
        var cutter = Box(scene, "remote cut", new Vector3(100, 0, 0), new Vector3(2));
        cutter.Brush = cutter.Brush!.WithOperation(BrushOperation.Subtractive);
        scene.RebuildStaticWorld(renderer);
        var source = new BrushPlaneCollisionSource(scene, new CharacterTuning());
        var volume = new Aabb(new Vector3(-1), new Vector3(1));
        source.BeginTick(volume, default);
        int revision = source.Revision;
        PublishMove(cutter, scene, renderer);
        source.BeginTick(volume, default);
        source.WorldSelections.ShouldBe(2);
        source.Revision.ShouldBeGreaterThan(revision);
        source.UncoveredCutBrushes.ShouldBe(0);
    }

    private static SceneNode Box(Scene scene, string name, Vector3 position, Vector3 half)
    {
        var node = scene.Root.CreateChild(name);
        node.Brush = Brush.CreateBox(-half, half);
        node.LocalPosition = position;
        return node;
    }

    private static void PublishMove(SceneNode node, Scene scene, FakeRenderer renderer)
    {
        int before = scene.StaticWorldCompileCount;
        node.LocalPosition += new Vector3(0, 0, .01f);
        SpinWait.SpinUntil(() =>
        {
            scene.ProcessStaticWorldCompilation(renderer, NullLogger.Instance);
            return scene.StaticWorldCompileCount > before;
        }, TimeSpan.FromSeconds(5)).ShouldBeTrue("the edited world must publish");
    }
}
