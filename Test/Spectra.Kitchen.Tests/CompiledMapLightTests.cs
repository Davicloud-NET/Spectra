using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// Lights through a real bake and a real load: the cooked level is lit as the
/// authored one is.
/// </summary>
public class CompiledMapLightTests
{
    // The sun, the lamp and its spot, the panel, the disc and the bulb.
    private const int FixtureLights = 6;

    [Fact]
    public void Every_light_arrives_on_its_node_with_every_field()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene(withLights: true));

        var authoredLights = new List<Light>();
        foreach (SceneNode authored in Nodes(level.Authored))
        {
            SceneNode node = Counterpart(level, authored);

            if (authored.Light is not { } expected)
            {
                node.Light.ShouldBeNull(authored.Name);
                continue;
            }

            authoredLights.Add(expected);
            Light actual = node.Light.ShouldNotBeNull(authored.Name);

            actual.Kind.ShouldBe(expected.Kind, authored.Name);
            actual.Color.ShouldBe(expected.Color, authored.Name);
            actual.Intensity.ShouldBe(expected.Intensity, authored.Name);
            actual.Range.ShouldBe(expected.Range, authored.Name);
            actual.Enabled.ShouldBe(expected.Enabled, authored.Name);
            actual.InnerAngle.ShouldBe(expected.InnerAngle, authored.Name);
            actual.OuterAngle.ShouldBe(expected.OuterAngle, authored.Name);
            actual.Width.ShouldBe(expected.Width, authored.Name);
            actual.Height.ShouldBe(expected.Height, authored.Name);
            actual.Radius.ShouldBe(expected.Radius, authored.Name);

            // The node aims the light, so its placement has to match too.
            node.WorldMatrix.ShouldBe(authored.WorldMatrix, authored.Name);
        }

        authoredLights.Count.ShouldBe(FixtureLights);
        level.Report.LightsLoaded.ShouldBe(FixtureLights);
        level.Scene.LightNodes.Count.ShouldBe(FixtureLights);

        // Each field was set away from its default somewhere, so none of the
        // comparisons above only compared two defaults.
        var unset = new Light();
        authoredLights.Select(light => light.Kind).Distinct().Count().ShouldBe(Enum.GetValues<LightKind>().Length);
        authoredLights.ShouldContain(light => light.Color != unset.Color);
        authoredLights.ShouldContain(light => light.Intensity != unset.Intensity);
        authoredLights.ShouldContain(light => light.Range != unset.Range);
        authoredLights.ShouldContain(light => light.Enabled != unset.Enabled);
        authoredLights.ShouldContain(light => light.InnerAngle != unset.InnerAngle);
        authoredLights.ShouldContain(light => light.OuterAngle != unset.OuterAngle);
        authoredLights.ShouldContain(light => light.Width != unset.Width);
        authoredLights.ShouldContain(light => light.Height != unset.Height);
        authoredLights.ShouldContain(light => light.Radius != unset.Radius);
    }

    [Fact]
    public void A_frame_of_the_cooked_level_gets_the_lights_a_frame_of_the_authored_one_gets()
    {
        // What reaches the shaders: positions, directions, radiance and shape.
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene(withLights: true));

        var camera = new Camera
        {
            Position = new Vector3(0f, 1.6f, 3f),
            Yaw = -MathF.PI / 2f,
            Pitch = 0f,
            AspectRatio = 16f / 9f,
        };

        var authored = new RenderView();
        level.Authored.BuildRenderView(camera, authored);

        var cooked = new RenderView();
        level.Scene.BuildRenderView(camera, cooked);

        // Five of the six: the disc is switched off.
        authored.LightCount.ShouldBe(FixtureLights - 1);
        cooked.Lights.ToArray().ShouldBe(authored.Lights.ToArray());
        cooked.LightsDropped.ShouldBe(0);
    }

    [Fact]
    public void A_light_on_a_part_brush_arrives_beside_the_brush()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene(withLights: true));

        SceneNode bulb = Nodes(level.Scene).Single(node => node.Name == "Bulb");

        bulb.Brush.ShouldNotBeNull();
        bulb.BrushKind.ShouldBe(BrushKind.Part);
        bulb.Light.ShouldNotBeNull().Range.ShouldBe(3f);
    }

    [Fact]
    public void A_light_that_is_switched_off_still_arrives()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene(withLights: true));

        Light disc = Nodes(level.Scene).Single(node => node.Name == "Disc").Light.ShouldNotBeNull();

        disc.Enabled.ShouldBeFalse();
        disc.Kind.ShouldBe(LightKind.Disc);
    }

    [Fact]
    public void Light_records_are_written_in_node_order()
    {
        using CookedLevel level = CookedLevel.Bake(MapFixture.Fresh().BuildScene(withLights: true));
        ScmapProbe map = ScmapProbe.Read(level.File);

        map.Lights.Select(light => map.NodeNames[(int)light.NodeIndex])
            .ShouldBe(["Sun", "Lamp", "Spot", "Panel", "Disc", "Bulb"]);
    }

    [Fact]
    public void A_level_bakes_the_same_world_with_and_without_its_lights()
    {
        MapFixture fixture = MapFixture.Fresh();
        using CookedLevel dark = CookedLevel.Bake(fixture.BuildScene());
        using CookedLevel lit = CookedLevel.Bake(fixture.BuildScene(withLights: true));

        ScmapProbe.Read(dark.File).Lights.ShouldBeEmpty();

        // The bulb is a part, so the chunks and the hulls do not move.
        foreach (uint section in new[]
        {
            ScmapFormat.AssetSection,
            ScmapFormat.ChunkDirectorySection,
            ScmapFormat.ChunkMeshSection,
            ScmapFormat.ChunkBspSection,
            ScmapFormat.CollisionSection,
        })
        {
            byte[] expected = ScmapSurgery.Body(dark.File, section);
            expected.Length.ShouldBeGreaterThan(0);

            ScmapSurgery.Body(lit.File, section).ShouldBe(expected, ScmapFormat.DescribeFourCc(section));
        }
    }

    private static SceneNode Counterpart(CookedLevel level, SceneNode authored)
    {
        level.Scene.TryFindById(authored.Id, out SceneNode? node).ShouldBeTrue(authored.Name);
        return node.ShouldNotBeNull();
    }

    private static IEnumerable<SceneNode> Nodes(SpectraEngine.Core.Scene.Scene scene) =>
        scene.Root.Traverse().Where(node => !ReferenceEquals(node, scene.Root));
}
