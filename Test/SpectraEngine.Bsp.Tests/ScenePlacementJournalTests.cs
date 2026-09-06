using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

public sealed class ScenePlacementJournalTests
{
    [Fact]
    public void Live_structural_edits_preserve_authored_bytes_and_remote_chunks()
    {
        var scene = new Scene();
        var renderer = new FakeRenderer();
        var box = Brush.CreateBox(new Vector3(-2), new Vector3(2));
        var remote = scene.Root.CreateChild("remote");
        remote.Brush = box;
        remote.LocalPosition = new(1000, 0, 0);
        var group = scene.Root.CreateChild("group");
        var nodes = Enumerable.Range(0, 5).Select(i =>
        {
            var node = group.CreateChild($"local{i}");
            node.Brush = box;
            node.LocalPosition = new(i * 2, 0, 0);
            return node;
        }).ToArray();
        scene.RebuildStaticWorld(renderer);
        var remoteCell = ChunkGrid.OwnerCell(new(box, remote.WorldMatrix));
        var original = scene.StaticWorld!.BuildMesh();

        Check(() => group.RemoveChild(nodes[2]));
        Check(() => group.InsertChild(2, nodes[2]));
        var restored = scene.StaticWorld!.BuildMesh();
        restored.Vertices.ShouldBe(original.Vertices);
        restored.Indices.ShouldBe(original.Indices);
        Check(() => group.InsertChild(0, nodes[4]));
        Check(() => nodes[1].AddChild(nodes[3]));
        Check(() => group.LocalPosition = new(1, 0, 0));
        Check(() => nodes[0].BrushKind = BrushKind.Part);
        Check(() => nodes[0].BrushKind = BrushKind.World);
        Check(() => group.InsertChild(1, nodes[1].Clone()));
        var other = new Scene();
        Check(() => other.Root.AddChild(nodes[4]));
        Check(() => group.InsertChild(0, nodes[4]));
        Check(() =>
        {
            nodes[0].LocalPosition += Vector3.UnitX;
            scene.CaptureStaticWorldPlacements(out var defect).ShouldNotBeNull();
            defect.ShouldBeNull();
            nodes[2].Brush = box.WithOperation(BrushOperation.Subtractive);
        });

        void Check(Action edit)
        {
            var previous = scene.StaticWorld!;
            previous.StorageChunks.TryGet(remoteCell, out var remoteBefore).ShouldBeTrue();
            edit();
            var timeout = Stopwatch.StartNew();
            while (scene.StaticWorld == previous)
            {
                scene.ProcessStaticWorldCompilation(renderer, NullLogger.Instance);
                if (timeout.Elapsed.TotalSeconds > 15) throw new TimeoutException("Scene publication did not arrive.");
                Thread.Sleep(1);
            }
            var actual = scene.StaticWorld!;
            actual.PatchBaseId.ShouldBe(previous.Id);
            var authored = scene.CaptureStaticWorldPlacements(out var defect)!;
            defect.ShouldBeNull();
            actual.Placements.ToArray().ShouldBe(authored.ToArray());
            var expected = CsgWorld.Build(authored).BuildMesh();
            var mesh = actual.BuildMesh();
            mesh.Vertices.ShouldBe(expected.Vertices);
            mesh.Indices.ShouldBe(expected.Indices);
            actual.StorageChunks.TryGet(remoteCell, out var remoteAfter).ShouldBeTrue();
            remoteAfter.ShouldBeSameAs(remoteBefore);
        }
    }
}
