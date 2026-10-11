using SpectraEngine.Core.Entities;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// What every input of every built-in class takes, and what every output
/// sends.
/// </summary>
// A script is typed from these, so each one is a decision. A new input or
// output fails here until it has a line.
public sealed class BuiltinParameterTypeTests
{
    // Class, in or out, name, and the value: none, a type, or a type with a
    // question mark for a value that may be left out.
    private static readonly string[] Decided =
    [
        "func_button in Use none",
        "func_button in Press none",
        "func_button out OnPressed none",
        "func_button out OnIn none",
        "func_button out OnOut none",

        "func_door in Open none",
        "func_door in Close none",
        "func_door in Toggle none",
        "func_door out OnOpen none",
        "func_door out OnClose none",
        "func_door out OnFullyOpen none",
        "func_door out OnFullyClosed none",

        "func_movelinear in Open none",
        "func_movelinear in Close none",
        "func_movelinear in SetPosition float",
        "func_movelinear out OnFullyOpen none",
        "func_movelinear out OnFullyClosed none",

        "logic_auto out OnMapSpawn none",

        "logic_branch in SetValue bool",
        "logic_branch in SetValueTest bool",
        "logic_branch in Toggle none",
        "logic_branch in ToggleTest none",
        "logic_branch in Test none",
        "logic_branch out OnTrue none",
        "logic_branch out OnFalse none",

        "logic_case in InValue string",
        "logic_case out OnCase01 none",
        "logic_case out OnCase02 none",
        "logic_case out OnCase03 none",
        "logic_case out OnCase04 none",
        "logic_case out OnCase05 none",
        "logic_case out OnCase06 none",
        "logic_case out OnCase07 none",
        "logic_case out OnCase08 none",
        "logic_case out OnCase09 none",
        "logic_case out OnCase10 none",
        "logic_case out OnCase11 none",
        "logic_case out OnCase12 none",
        "logic_case out OnCase13 none",
        "logic_case out OnCase14 none",
        "logic_case out OnCase15 none",
        "logic_case out OnCase16 none",
        "logic_case out OnDefault string",

        "logic_compare in SetValue float",
        "logic_compare in SetValueCompare float",
        "logic_compare in SetCompareValue float",
        "logic_compare in Compare none",
        "logic_compare out OnLessThan none",
        "logic_compare out OnEqualTo none",
        "logic_compare out OnNotEqualTo none",
        "logic_compare out OnGreaterThan none",

        "logic_relay in Trigger none",
        "logic_relay in Enable none",
        "logic_relay in Disable none",
        "logic_relay in Toggle none",
        "logic_relay out OnTrigger none",

        "logic_timer in Enable none",
        "logic_timer in Disable none",
        "logic_timer in Toggle none",
        "logic_timer in ResetTimer none",
        "logic_timer in FireTimer none",
        "logic_timer in RefireTime float",
        "logic_timer out OnTimer none",

        "math_counter in Add float?",
        "math_counter in Subtract float?",
        "math_counter in SetValue float",
        "math_counter in SetValueNoFire float",
        "math_counter in SetHitMax float",
        "math_counter in SetHitMin float",
        "math_counter in GetValue none",
        "math_counter out OutValue float",
        "math_counter out OnHitMax none",
        "math_counter out OnHitMin none",

        "point_sound in Play none",
        "point_sound in Stop none",
        "point_sound in SetVolume float",
        "point_sound in SetPitch float",
        "point_sound out OnEnded none",
        "point_sound out OnMarker string",

        "trigger_multiple in Enable none",
        "trigger_multiple in Disable none",
        "trigger_multiple in Toggle none",
        "trigger_multiple out OnStartTouch none",
        "trigger_multiple out OnEndTouch none",
        "trigger_multiple out OnTrigger none",

        "trigger_once in Enable none",
        "trigger_once in Disable none",
        "trigger_once in Toggle none",
        "trigger_once out OnStartTouch none",
        "trigger_once out OnTrigger none",

        "trigger_teleport in Enable none",
        "trigger_teleport in Disable none",
        "trigger_teleport in Toggle none",
        "trigger_teleport out OnStartTouch none",
        "trigger_teleport out OnEndTouch none",
    ];

    [Fact]
    public void Every_input_and_output_of_every_built_in_class_has_a_decided_type()
    {
        var declared = new List<string>();
        foreach (EntitySchema schema in BuiltinEntities.Schemas)
        {
            foreach (EntityPort input in schema.Inputs)
                declared.Add($"{schema.ClassName} in {input.Name} {input.Parameter}");
            foreach (EntityPort output in schema.Outputs)
                declared.Add($"{schema.ClassName} out {output.Name} {output.Parameter}");
        }

        declared.ShouldBe(Decided);
    }

    [Fact]
    public void No_built_in_class_leaves_a_type_unsaid()
    {
        // Only a file from an older build may do that.
        foreach (EntitySchema schema in BuiltinEntities.Schemas)
        {
            schema.Inputs.ShouldAllBe(input => input.Parameter.IsSaid, schema.ClassName);
            schema.Outputs.ShouldAllBe(output => output.Parameter.IsSaid, schema.ClassName);
        }
    }

    [Fact]
    public void Only_an_input_has_a_value_that_may_be_left_out()
    {
        foreach (EntitySchema schema in BuiltinEntities.Schemas)
            schema.Outputs.ShouldAllBe(output => !output.Parameter.IsOptional, schema.ClassName);
    }

    [Fact]
    public void The_two_classes_with_no_wiring_declare_none()
    {
        InfoPlayerStart.SpectraSchema.Inputs.ShouldBeEmpty();
        InfoPlayerStart.SpectraSchema.Outputs.ShouldBeEmpty();
        InfoTeleportDestination.SpectraSchema.Inputs.ShouldBeEmpty();
        InfoTeleportDestination.SpectraSchema.Outputs.ShouldBeEmpty();
    }
}
