using Microsoft.CodeAnalysis;
using SpectraEngine.Core.Entities;
using System;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// What the generator makes of the type an input or an output says it
/// carries.
/// </summary>
public sealed class EntityGeneratorParameterTests
{
    private const string Ns = "global::SpectraEngine.Core.Entities";

    // Every member of the enum: the generator keeps its own table of what a
    // wire can carry and cannot see Core's.
    public static TheoryData<KeyvalueType> EveryType => new(Enum.GetValues<KeyvalueType>());

    [Theory]
    [MemberData(nameof(EveryType))]
    public void An_input_and_an_output_may_carry_the_types_a_wire_can_and_no_other(KeyvalueType type)
    {
        GeneratorRun run = GeneratorHarness.Run(Thing(
            $"[EntityOutput(Parameter = KeyvalueType.{type})]",
            $"[EntityInput(\"Set\", Parameter = KeyvalueType.{type})]",
            "FireOnSet(default);"));

        if (!ParameterType.CanCarry(type))
        {
            run.DiagnosticIds.ShouldBe(["SPE009", "SPE009"]);
            return;
        }

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();

        string emitted = Compact(run.OnlySource());
        emitted.ShouldContain($"new{Ns}.EntityPort(\"Set\",{Ns}.ParameterType.Of({Ns}.KeyvalueType.{type})),");
        emitted.ShouldContain($"new{Ns}.EntityPort(\"OnSet\",{Ns}.ParameterType.Of({Ns}.KeyvalueType.{type})),");
    }

    [Fact]
    public void A_type_no_wire_carries_is_reported_at_the_attribute_that_says_it()
    {
        string source = Thing("[EntityOutput]", "[EntityInput(\"Set\", Parameter = KeyvalueType.Choices)]", "");

        Diagnostic refused = GeneratorHarness.Run(source).Diagnostics.ShouldHaveSingleItem();

        refused.Id.ShouldBe("SPE009");
        refused.GetMessage().ShouldContain("Input 'Set' declares a parameter of KeyvalueType.Choices");
        refused.GetMessage().ShouldContain("Use Bool, Int, Float, String");
        Quoted(source, refused).ShouldBe("EntityInput(\"Set\", Parameter = KeyvalueType.Choices)");
    }

    [Fact]
    public void A_refused_output_is_named_by_its_member()
    {
        string source = Thing("[EntityOutput(Parameter = KeyvalueType.AssetSound)]", "[EntityInput(\"Set\")]", "");

        Diagnostic refused = GeneratorHarness.Run(source).Diagnostics.ShouldHaveSingleItem();

        refused.GetMessage().ShouldContain("Output 'OnSet' declares a parameter of KeyvalueType.AssetSound");
        Quoted(source, refused).ShouldBe("EntityOutput(Parameter = KeyvalueType.AssetSound)");
    }

    [Fact]
    public void A_refused_type_leaves_the_input_taking_nothing_and_the_class_compiling()
    {
        GeneratorRun run = GeneratorHarness.Run(Thing(
            "[EntityOutput]", "[EntityInput(\"Set\", Parameter = KeyvalueType.Flags)]", "FireOnSet();"));

        run.DiagnosticIds.ShouldBe(["SPE009"]);
        run.CompileErrors().ShouldBeEmpty();
        Compact(run.OnlySource()).ShouldContain($"new{Ns}.EntityPort(\"Set\",{Ns}.ParameterType.None),");
    }

    [Fact]
    public void An_input_and_an_output_that_say_nothing_carry_no_value()
    {
        GeneratorRun run = GeneratorHarness.Run(Thing("[EntityOutput]", "[EntityInput(\"Set\")]", "FireOnSet();"));

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();

        string emitted = Compact(run.OnlySource());
        emitted.ShouldContain($"new{Ns}.EntityPort(\"Set\",{Ns}.ParameterType.None),");
        emitted.ShouldContain($"new{Ns}.EntityPort(\"OnSet\",{Ns}.ParameterType.None),");
    }

    [Fact]
    public void An_input_that_may_leave_its_value_out_says_so_in_the_schema()
    {
        GeneratorRun run = GeneratorHarness.Run(Thing(
            "[EntityOutput]",
            "[EntityInput(\"Set\", Parameter = KeyvalueType.Float, CanOmitParameter = true)]",
            ""));

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        Compact(run.OnlySource()).ShouldContain(
            $"new{Ns}.EntityPort(\"Set\",{Ns}.ParameterType.Optional({Ns}.KeyvalueType.Float)),");
    }

    [Fact]
    public void Leaving_out_a_value_the_input_never_said_it_takes_is_refused()
    {
        string source = Thing("[EntityOutput]", "[EntityInput(\"Set\", CanOmitParameter = true)]", "");
        GeneratorRun run = GeneratorHarness.Run(source);

        Diagnostic refused = run.Diagnostics.ShouldHaveSingleItem();
        refused.Id.ShouldBe("SPE010");
        refused.GetMessage().ShouldContain("Input 'Set' sets CanOmitParameter and declares no Parameter");
        Quoted(source, refused).ShouldBe("EntityInput(\"Set\", CanOmitParameter = true)");
        Compact(run.OnlySource()).ShouldContain($"new{Ns}.EntityPort(\"Set\",{Ns}.ParameterType.None),");
    }

    [Fact]
    public void A_refused_type_that_may_be_left_out_is_reported_once_for_the_type()
    {
        GeneratorRun run = GeneratorHarness.Run(Thing(
            "[EntityOutput]",
            "[EntityInput(\"Set\", Parameter = KeyvalueType.Distance, CanOmitParameter = true)]",
            ""));

        run.DiagnosticIds.ShouldBe(["SPE009"]);
        Compact(run.OnlySource()).ShouldContain($"new{Ns}.EntityPort(\"Set\",{Ns}.ParameterType.None),");
    }

    [Fact]
    public void An_output_that_sends_a_number_is_fired_with_the_number_and_writes_it_as_a_keyvalue_would()
    {
        GeneratorRun run = GeneratorHarness.Run(Thing(
            "[EntityOutput(Parameter = KeyvalueType.Float)]",
            "[EntityInput(\"Set\")]",
            "FireOnSet(1.5f, context.Activator);"));

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();
        Compact(run.OnlySource()).ShouldContain(
            $"privatevoidFireOnSet(floatvalue,{Ns}.Entity?activator=null)=>" +
            $"FireOutput(\"OnSet\",activator,{Ns}.KeyvalueWire.Format(value));");
    }

    [Fact]
    public void An_output_that_sends_text_sends_it_as_it_is()
    {
        GeneratorRun run = GeneratorHarness.Run(Thing(
            "[EntityOutput(Parameter = KeyvalueType.String)]",
            "[EntityInput(\"Set\")]",
            "FireOnSet(context.Parameter);"));

        run.CompileErrors().ShouldBeEmpty();
        Compact(run.OnlySource()).ShouldContain(
            $"privatevoidFireOnSet(stringvalue,{Ns}.Entity?activator=null)=>FireOutput(\"OnSet\",activator,value);");
    }

    [Fact]
    public void An_output_that_sends_a_node_sends_the_empty_text_for_no_node()
    {
        GeneratorRun run = GeneratorHarness.Run(Thing(
            "[EntityOutput(Parameter = KeyvalueType.NodeRef)]",
            "[EntityInput(\"Set\")]",
            "FireOnSet(System.Guid.Empty);"));

        run.CompileErrors().ShouldBeEmpty();
        Compact(run.OnlySource()).ShouldContain(
            $"value==global::System.Guid.Empty?\"\":{Ns}.KeyvalueWire.Format(value));");
    }

    [Fact]
    public void An_output_that_sends_nothing_cannot_be_handed_a_value()
    {
        // So what the schema says an output sends is what the class sends.
        GeneratorRun run = GeneratorHarness.Run(Thing(
            "[EntityOutput]", "[EntityInput(\"Set\")]", "FireOnSet(context.Activator, \"5\");"));

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldHaveSingleItem().ShouldStartWith("CS1501");
    }

    [Fact]
    public void An_output_that_sends_a_value_cannot_be_fired_without_one()
    {
        GeneratorRun run = GeneratorHarness.Run(Thing(
            "[EntityOutput(Parameter = KeyvalueType.Float)]", "[EntityInput(\"Set\")]", "FireOnSet();"));

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldHaveSingleItem().ShouldStartWith("CS7036");
    }

    // One output, OnSet, and one input, Set, whose method is the statement given.
    private static string Thing(string outputAttribute, string inputAttribute, string statement) => $$"""
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("test_thing")]
        public sealed partial class TestThing : Entity
        {
            {{outputAttribute}}
            public const string OnSet = nameof(OnSet);

            {{inputAttribute}}
            private void Set(ref EntityInputContext context)
            {
                {{statement}}
            }
        }
        """;

    // Without the layout, so the lines of one declaration can be read as one.
    private static string Compact(string source) => string.Concat(source.Where(c => !char.IsWhiteSpace(c)));

    // The source text the diagnostic points at.
    private static string Quoted(string source, Diagnostic diagnostic) =>
        source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);
}
