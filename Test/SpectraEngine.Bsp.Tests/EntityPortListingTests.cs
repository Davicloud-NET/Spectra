using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// How <c>ent_show</c> prints what an input takes and an output sends, beside
/// its name.
/// </summary>
public sealed class EntityPortListingTests
{
    private static readonly EntitySchema Gate = new(
        "gate",
        inputs:
        [
            new EntityPort("Open", ParameterType.None),
            new EntityPort("SetValue", ParameterType.Of(KeyvalueType.Float)),
            new EntityPort("Add", ParameterType.Optional(KeyvalueType.Float)),
            new EntityPort("Aim", ParameterType.Of(KeyvalueType.Vec3)),
            new EntityPort("Say", ParameterType.Of(KeyvalueType.String)),
            new EntityPort("Legacy", ParameterType.NotSaid),
        ],
        outputs:
        [
            new EntityPort("OutValue", ParameterType.Of(KeyvalueType.Float)),
            new EntityPort("OnDone", ParameterType.None),
            new EntityPort("OnOld", ParameterType.NotSaid),
        ]);

    [Fact]
    public void An_input_is_listed_with_the_type_it_takes_in_brackets()
    {
        (Scene scene, _) = Level(Gate);

        Show(scene).ShouldContain("gate1 inputs: Open() SetValue(float) Add(float?) Aim(vec3) Say(string) Legacy");
    }

    [Fact]
    public void An_output_is_listed_with_the_type_it_sends_whether_wired_or_not()
    {
        (Scene scene, SceneNode gate) = Level(Gate);
        EntityRuntime.Wire(gate, "OutValue", "gate1", "SetValue");
        EntityRuntime.Wire(gate, "OnDone", "gate1", "Open", delay: 2f);

        Show(scene).ShouldBe(
        [
            $"gate1: gate, node {gate.Id:D}",
            "gate1 not running: no live state",
            "gate1 output OutValue(float) -> gate1.SetValue fires=unlimited, 1 entity named gate1",
            "gate1 output OnDone() -> gate1.Open in=2s fires=unlimited, 1 entity named gate1",
            "gate1 output OnOld: not wired",
            "gate1 inputs: Open() SetValue(float) Add(float?) Aim(vec3) Say(string) Legacy",
        ]);
    }

    [Fact]
    public void A_class_from_a_file_too_old_to_say_is_listed_by_name_alone()
    {
        // Empty brackets would claim it takes nothing, which nobody said.
        var old = new EntitySchema(
            "gate", inputs: EntityPort.Named("Open", "SetValue"), outputs: EntityPort.Named("OnDone"));
        (Scene scene, _) = Level(old);

        string[] rows = Show(scene);

        rows.ShouldContain("gate1 output OnDone: not wired");
        rows.ShouldContain("gate1 inputs: Open SetValue");
    }

    [Fact]
    public void A_wire_on_an_output_the_class_does_not_declare_is_listed_with_no_type()
    {
        (Scene scene, SceneNode gate) = Level(Gate);
        EntityRuntime.Wire(gate, "OnDon", "gate1", "Open");

        Show(scene).ShouldContain(
            "gate1 output OnDon -> gate1.Open fires=unlimited, 1 entity named gate1, gate has no output OnDon");
    }

    private static (Scene Scene, SceneNode Gate) Level(EntitySchema schema)
    {
        var scene = new Scene("Gates")
        {
            EntitySchemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write([schema])),
        };

        return (scene, EntityRuntime.Place(scene.Root, "gate1", "gate"));
    }

    private static string[] Show(Scene scene)
    {
        var console = new SpectraConsole();
        EntityConsoleCommands.Register(console.Commands, new EntityWatch(console.Output));

        console.Execute("ent_show gate1", new ConsoleFrame(scene, null));
        return [.. console.Output.Drain().Select(line => line.Text)];
    }
}
