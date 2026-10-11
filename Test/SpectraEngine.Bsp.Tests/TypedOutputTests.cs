using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// An output fired with a typed value, which is what a generated
/// <c>Fire</c> method does: each wire carries the text
/// <see cref="ParameterText"/> writes, and a value it refuses fires nothing.
/// </summary>
public sealed class TypedOutputTests
{
    private const float Tick = 1f / 60f;

    private static readonly Guid Door = Guid.Parse("3f2a9c1e-5b7d-4e0a-9c41-0d2f6a8b1c33");
    private static readonly ParameterType Number = ParameterType.Of(KeyvalueType.Float);

    private readonly List<string> _heard = [];
    private readonly CapturingLogger _logger = new();

    [Fact]
    public void Each_wire_carries_the_text_the_one_converter_writes_for_the_value()
    {
        (KeyvalueType Type, ParameterValue Value, string Text)[] cases =
        [
            (KeyvalueType.Float, ParameterValue.Of(0.1f), "0.1"),
            (KeyvalueType.Float, ParameterValue.Of(1f / 3f), "0.33333334"),
            (KeyvalueType.Int, ParameterValue.Of(16777217), "16777217"),
            (KeyvalueType.Bool, ParameterValue.Of(true), "1"),
            (KeyvalueType.Vec2, ParameterValue.Of(new Vector2(0.5f, -8f)), "0.5 -8"),
            (KeyvalueType.Vec3, ParameterValue.Of(new Vector3(1.5f, -2f, 0.25f)), "1.5 -2 0.25"),
            (KeyvalueType.Color, ParameterValue.Of(new Vector3(1f, 0.5f, 0.25f)), "1 0.5 0.25"),
            (KeyvalueType.Vec4, ParameterValue.Of(new Vector4(1f, 2f, 3f, 0.125f)), "1 2 3 0.125"),
            (KeyvalueType.String, ParameterValue.Of("two words"), "two words"),
            (KeyvalueType.TargetName, ParameterValue.Of("door*"), "door*"),
            (KeyvalueType.NodeRef, ParameterValue.Of(Door), "3f2a9c1e-5b7d-4e0a-9c41-0d2f6a8b1c33"),
        ];

        foreach ((KeyvalueType type, ParameterValue value, string text) in cases)
        {
            _heard.Clear();
            (EntityWorld world, Entity sender, _) = Level();

            sender.FireOutput("OnGo", ParameterType.Of(type), in value).ShouldBeTrue();
            world.Tick(Tick);

            ParameterText.TryFormat(ParameterType.Of(type), in value, out string written).ShouldBeTrue();
            written.ShouldBe(text);
            _heard.ShouldBe([$"target:Take:{text}:sender:sender"], type.ToString());
        }
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void A_number_that_is_not_finite_fires_nothing_and_throws_nothing(float value)
    {
        (EntityWorld world, Entity sender, _) = Level();
        var trace = new RecordingEntityTrace();
        world.Trace = trace;

        sender.FireOutput("OnGo", Number, ParameterValue.Of(value)).ShouldBeFalse();
        world.Tick(Tick);

        _heard.ShouldBeEmpty();
        trace.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void A_vector_with_a_number_that_is_not_finite_fires_nothing()
    {
        (EntityWorld world, Entity sender, _) = Level();

        sender.FireOutput(
            "OnGo", ParameterType.Of(KeyvalueType.Vec3), ParameterValue.Of(new Vector3(1f, float.NaN, 0f)))
            .ShouldBeFalse();
        world.Tick(Tick);

        _heard.ShouldBeEmpty();
    }

    [Fact]
    public void A_refused_value_is_logged_once_for_its_output_however_often_it_is_fired()
    {
        (_, Entity sender, _) = Level();

        for (int i = 0; i < 3; i++)
            sender.FireOutput("OnGo", Number, ParameterValue.Of(float.NaN));

        _logger.MessagesAt(LogLevel.Warning).ShouldBe(
            ["Entity 'sender' (recorder) fired OnGo(float) with a value it cannot send, so nothing was sent."]);

        sender.FireOutput("OnStop", Number, ParameterValue.Of(float.NaN));

        _logger.MessagesAt(LogLevel.Warning).Count.ShouldBe(2);
    }

    [Fact]
    public void A_good_value_after_a_refused_one_is_sent()
    {
        (EntityWorld world, Entity sender, _) = Level();
        sender.FireOutput("OnGo", Number, ParameterValue.Of(float.NaN));

        sender.FireOutput("OnGo", Number, ParameterValue.Of(2f)).ShouldBeTrue();
        world.Tick(Tick);

        _heard.ShouldBe(["target:Take:2:sender:sender"]);
    }

    [Fact]
    public void No_node_is_sent_as_the_empty_text_in_place_of_the_wires_own_parameter()
    {
        (EntityWorld world, Entity sender, _) = Level();

        sender.FireOutput("OnGo", ParameterType.Of(KeyvalueType.NodeRef), ParameterValue.Of(Guid.Empty))
            .ShouldBeTrue();
        world.Tick(Tick);

        _heard.ShouldBe(["target:Take::sender:sender"]);
    }

    [Fact]
    public void No_value_on_an_output_that_sends_none_leaves_the_wire_its_own_parameter()
    {
        (EntityWorld world, Entity sender, _) = Level();

        sender.FireOutput("OnGo", ParameterType.None, ParameterValue.None).ShouldBeTrue();
        world.Tick(Tick);

        _heard.ShouldBe(["target:Take:authored:sender:sender"]);
    }

    [Fact]
    public void A_value_on_an_output_that_sends_none_is_refused()
    {
        (EntityWorld world, Entity sender, _) = Level();

        sender.FireOutput("OnGo", ParameterType.None, ParameterValue.Of(5)).ShouldBeFalse();
        world.Tick(Tick);

        _heard.ShouldBeEmpty();
        _logger.MessagesAt(LogLevel.Warning).ShouldBe(
            ["Entity 'sender' (recorder) fired OnGo() with a value it cannot send, so nothing was sent."]);
    }

    [Fact]
    public void A_value_of_another_kind_than_the_output_sends_is_refused()
    {
        (EntityWorld world, Entity sender, _) = Level();

        sender.FireOutput("OnGo", Number, ParameterValue.Of("five")).ShouldBeFalse();
        world.Tick(Tick);

        _heard.ShouldBeEmpty();
    }

    [Fact]
    public void The_activator_travels_with_the_value()
    {
        (EntityWorld world, Entity sender, Entity target) = Level();

        sender.FireOutput("OnGo", Number, ParameterValue.Of(2f), target);
        world.Tick(Tick);

        _heard.ShouldBe(["target:Take:2:target:sender"]);
    }

    // A sender whose OnGo is wired to a recorder, with a parameter of the
    // wire's own.
    private (EntityWorld World, Entity Sender, Entity Target) Level()
    {
        var scene = new Scene("Typed");
        SceneNode sender = EntityRuntime.Place(scene.Root, "sender", "recorder");
        SceneNode target = EntityRuntime.Place(scene.Root, "target", "recorder");
        EntityRuntime.Wire(sender, "OnGo", "target", "Take", "authored");

        var world = new EntityWorld(scene, _logger, EntityRuntime.Catalog(_heard));
        world.Activate();
        return (world, EntityRuntime.Live(world, sender), EntityRuntime.Live(world, target));
    }
}
