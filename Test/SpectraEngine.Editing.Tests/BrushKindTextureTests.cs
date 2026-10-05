using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Converting a block to a part keeps the texture where it was. The world
/// path maps UVs over world-space vertices and the part path over local ones.
/// </summary>
public sealed class BrushKindTextureTests
{
    private const float Tolerance = 2e-3f;

    private readonly record struct MeshVertex(Vector3 Position, Vector3 Normal, Vector2 Uv);

    public static TheoryData<float, float, float> Rotations => new()
    {
        { 0f, 0f, 0f },
        { 30f, 0f, 0f },
        { 40f, 25f, 10f },
    };

    private static (Scene Scene, CompilingRenderer Renderer, SceneEditorHost Host) NewEditor()
    {
        var scene = new Scene("texture");
        var renderer = new CompilingRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));

        var host = new SceneEditorHost(
            NullLoggerFactory.Instance, scene, renderer,
            new InputManager(NullLogger<InputManager>.Instance));

        return (scene, renderer, host);
    }

    // Off the grid, so a missing bake shows as a shift even with no rotation.
    private static SceneNode AddBlock(Scene scene, float yaw, float pitch, float roll)
    {
        SceneNode node = scene.Root.CreateChild("Block");
        node.LocalPosition = new Vector3(0.3f, 1.7f, -2.4f);
        node.LocalRotation = Quaternion.CreateFromYawPitchRoll(
            float.DegreesToRadians(yaw), float.DegreesToRadians(pitch), float.DegreesToRadians(roll));

        // One face with its own scale and offset, one with stored axes.
        Brush brush = Brush.CreateBox(new Vector3(-1f, -0.5f, -2f), new Vector3(1f, 0.5f, 2f));
        brush = brush.WithFaceSurface(0, FaceSurface.Default.WithAxes(
            Vector3.Zero, Vector3.Zero, 0.25f, -0.5f, 2f, 0.5f));
        brush = brush.WithFaceSurface(4, new FaceSurface(
            MaterialRef.Default, Vector3.UnitX, Vector3.UnitY, 0.1f, 0.2f, 1.5f, 1f));
        node.Brush = brush;
        return node;
    }

    private static List<MeshVertex> WorldVertices(Scene scene, CompilingRenderer renderer)
    {
        scene.RebuildStaticWorld(renderer);
        (float[] vertices, _) = scene.StaticWorld!.BuildMesh();
        return Unpack(vertices, Matrix4x4.Identity);
    }

    // The arrays PartBrushMeshCache uploads, taken to world space.
    private static List<MeshVertex> PartVertices(SceneNode node)
    {
        var all = new List<MeshVertex>();
        foreach (ChunkSubmesh submesh in ChunkMeshBuilder.BuildSubmeshes(VertexSnapper.Snap(node.Brush!.LocalFaces)))
            all.AddRange(Unpack(submesh.Vertices, node.WorldMatrix));
        return all;
    }

    private static List<MeshVertex> Unpack(float[] vertices, Matrix4x4 toWorld)
    {
        var unpacked = new List<MeshVertex>(vertices.Length / 8);
        for (int i = 0; i < vertices.Length; i += 8)
        {
            unpacked.Add(new MeshVertex(
                Vector3.Transform(new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]), toWorld),
                Vector3.TransformNormal(new Vector3(vertices[i + 3], vertices[i + 4], vertices[i + 5]), toWorld),
                new Vector2(vertices[i + 6], vertices[i + 7])));
        }

        return unpacked;
    }

    // One way only: the carve splits a face at a chunk border, so the world
    // mesh can hold vertices the part mesh has no use for.
    private static void ShouldMatch(List<MeshVertex> before, List<MeshVertex> after)
    {
        after.ShouldNotBeEmpty();

        foreach (MeshVertex vertex in after)
        {
            int match = before.FindIndex(candidate =>
                Vector3.Distance(candidate.Position, vertex.Position) < Tolerance &&
                Vector3.Dot(candidate.Normal, vertex.Normal) > 0.999f);

            match.ShouldBeGreaterThanOrEqualTo(0, $"no vertex at {vertex.Position} facing {vertex.Normal}");
            Vector2.Distance(before[match].Uv, vertex.Uv).ShouldBeLessThan(
                Tolerance, $"the texture moved at {vertex.Position} facing {vertex.Normal}");
        }
    }

    [Theory]
    [MemberData(nameof(Rotations))]
    public void A_block_keeps_its_uvs_when_it_becomes_a_part(float yaw, float pitch, float roll)
    {
        (Scene scene, CompilingRenderer renderer, SceneEditorHost host) = NewEditor();
        SceneNode node = AddBlock(scene, yaw, pitch, roll);
        List<MeshVertex> before = WorldVertices(scene, renderer);

        scene.Selection.Select(node);
        host.Apply(EditorHostCommand.ToggleBrushKind);

        node.BrushKind.ShouldBe(BrushKind.Part);
        ShouldMatch(before, PartVertices(node));
    }

    [Theory]
    [MemberData(nameof(Rotations))]
    public void A_block_keeps_its_uvs_when_it_is_made_an_entity(float yaw, float pitch, float roll)
    {
        var rig = new EntityEditRig();
        SceneNode node = AddBlock(rig.Scene, yaw, pitch, roll);
        List<MeshVertex> before = WorldVertices(rig.Scene, rig.Renderer);

        rig.Make(EntityEditRig.Door, node).Applied.ShouldBeTrue();

        node.BrushKind.ShouldBe(BrushKind.Part);
        ShouldMatch(before, PartVertices(node));
    }

    [Fact]
    public void The_properties_panel_converts_without_moving_the_texture_either()
    {
        (Scene scene, CompilingRenderer renderer, _) = NewEditor();
        SceneNode node = AddBlock(scene, 30f, 0f, 0f);
        List<MeshVertex> before = WorldVertices(scene, renderer);

        var undo = new UndoStack(scene);
        PropertyEditor.Apply(undo, [node], new PropertyEdit { Id = PropertyId.BrushKind, Text = "Part" }).ShouldBe(1);

        node.BrushKind.ShouldBe(BrushKind.Part);
        undo.UndoCount.ShouldBe(1);
        ShouldMatch(before, PartVertices(node));
    }

    [Fact]
    public void A_block_under_a_turned_group_keeps_its_uvs_too()
    {
        (Scene scene, CompilingRenderer renderer, SceneEditorHost host) = NewEditor();
        SceneNode group = scene.Root.CreateChild("Group");
        group.LocalPosition = new Vector3(4.2f, 0f, 1.1f);
        group.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, float.DegreesToRadians(55f));

        SceneNode node = AddBlock(scene, 0f, 20f, 0f);
        scene.Root.RemoveChild(node);
        group.AddChild(node);
        List<MeshVertex> before = WorldVertices(scene, renderer);

        scene.Selection.Select(node);
        host.Apply(EditorHostCommand.ToggleBrushKind);

        ShouldMatch(before, PartVertices(node));
    }

    [Fact]
    public void Undoing_the_conversion_gives_back_the_same_brush()
    {
        (Scene scene, _, SceneEditorHost host) = NewEditor();
        SceneNode node = AddBlock(scene, 30f, 0f, 0f);
        Brush authored = node.Brush!;

        scene.Selection.Select(node);
        host.Apply(EditorHostCommand.ToggleBrushKind);
        host.UndoDepth.ShouldBe(1);

        host.Apply(EditorHostCommand.Undo);

        node.BrushKind.ShouldBe(BrushKind.World);
        node.Brush.ShouldBeSameAs(authored);
    }

    [Fact]
    public void A_part_going_back_to_a_block_keeps_the_brush_it_has()
    {
        // Stored axes are texture-locked on both paths, so nothing is baked.
        (Scene scene, _, SceneEditorHost host) = NewEditor();
        SceneNode node = AddBlock(scene, 30f, 0f, 0f);
        scene.Selection.Select(node);
        host.Apply(EditorHostCommand.ToggleBrushKind);
        Brush baked = node.Brush!;

        host.Apply(EditorHostCommand.ToggleBrushKind);

        node.BrushKind.ShouldBe(BrushKind.World);
        node.Brush.ShouldBeSameAs(baked);
    }
}
