namespace SpectraEngine.Entities.Tests;

public sealed class EntityGeneratorDiagnosticTests
{
    [Fact]
    public void A_class_that_is_not_partial_is_refused()
    {
        GeneratorRun run = GeneratorHarness.Run(Fixtures.NotPartial);

        run.DiagnosticIds.ShouldContain("SPE001");
    }

    [Fact]
    public void A_class_that_is_not_partial_has_nothing_emitted_for_it()
    {
        // The only refusal that stops emission; the others drop one member.
        GeneratorRun run = GeneratorHarness.Run(Fixtures.NotPartial);

        run.SourceCount.ShouldBe(0);
    }

    [Fact]
    public void Two_classes_claiming_one_class_name_are_refused()
    {
        GeneratorRun run = GeneratorHarness.Run(Fixtures.DuplicateClassName);

        run.DiagnosticIds.ShouldContain("SPE002");

        // Reported once, on the second declaration.
        run.DiagnosticIds.Count(id => id == "SPE002").ShouldBe(1);
        run.Diagnostics.Single(d => d.Id == "SPE002").GetMessage().ShouldContain("SecondThing");
    }

    [Fact]
    public void A_keyvalue_on_a_type_nothing_is_inferred_from_is_refused()
    {
        GeneratorRun run = GeneratorHarness.Run(Fixtures.UnsupportedKeyvalueType);

        run.DiagnosticIds.ShouldContain("SPE003");
        run.Diagnostics.Single(d => d.Id == "SPE003").GetMessage().ShouldContain("State the type explicitly");
    }

    [Fact]
    public void A_keyvalue_whose_stated_type_the_member_cannot_carry_is_refused()
    {
        // Color is read as a Vector3, so a float member cannot hold one.
        GeneratorRun run = GeneratorHarness.Run(Fixtures.KeyvalueTypeMismatch);

        run.DiagnosticIds.ShouldContain("SPE006");
    }

    [Fact]
    public void A_distance_on_a_member_that_is_not_a_float_is_refused()
    {
        const string source = """
            using SpectraEngine.Core.Entities;

            namespace TestGame.Entities;

            [SpectraEntity("env_beacon")]
            public sealed partial class EnvBeacon : Entity
            {
                [Keyvalue("reach", Type = KeyvalueType.Distance)]
                public int Reach { get; set; }
            }
            """;

        GeneratorRun run = GeneratorHarness.Run(source);

        run.DiagnosticIds.ShouldBe(["SPE006"]);
        run.Diagnostics.Single().GetMessage()
            .ShouldContain("KeyvalueType.Distance, which is read as 'float'");
    }

    [Fact]
    public void A_keyvalue_the_binder_cannot_assign_to_is_refused()
    {
        GeneratorRun run = GeneratorHarness.Run(Fixtures.KeyvalueNotAssignable);

        run.DiagnosticIds.ShouldContain("SPE007");
    }

    [Fact]
    public void An_input_that_is_not_shaped_the_way_the_dispatch_calls_one_is_refused()
    {
        GeneratorRun run = GeneratorHarness.Run(Fixtures.InvalidInputSignature);

        run.DiagnosticIds.ShouldContain("SPE004");
        run.Diagnostics.Single(d => d.Id == "SPE004").GetMessage()
            .ShouldContain("void Trigger(ref EntityInputContext context)");
    }

    [Fact]
    public void A_keyvalue_named_targetname_is_refused()
    {
        // targetname is SceneNode.Name; a keyvalue would be a second copy.
        GeneratorRun run = GeneratorHarness.Run(Fixtures.ReservedKeyvalueName);

        run.DiagnosticIds.ShouldContain("SPE005");
    }

    [Fact]
    public void A_keyvalue_named_TargetName_in_any_casing_is_refused()
    {
        const string source = """
            using SpectraEngine.Core.Entities;

            namespace TestGame.Entities;

            [SpectraEntity("logic_thing")]
            public sealed partial class LogicThing : Entity
            {
                [Keyvalue("TargetName")]
                public string Who { get; set; } = "";
            }
            """;

        GeneratorHarness.Run(source).DiagnosticIds.ShouldContain("SPE005");
    }

    [Fact]
    public void A_placement_the_generator_has_no_name_for_is_refused()
    {
        GeneratorRun run = GeneratorHarness.Run(Fixtures.UnknownPlacement);

        run.DiagnosticIds.ShouldBe(["SPE008"]);

        string message = run.Diagnostics.Single().GetMessage();
        message.ShouldContain("TriggerThing");
        message.ShouldContain("placement 9");
    }

    [Fact]
    public void A_refused_placement_is_the_only_error_the_class_gets()
    {
        // The rest is still emitted, so nothing else fails to compile.
        GeneratorRun run = GeneratorHarness.Run(Fixtures.UnknownPlacement);

        run.SourceCount.ShouldBe(1);
        run.CompileErrors().ShouldBeEmpty();
    }

    [Fact]
    public void A_refused_member_does_not_stop_the_rest_of_the_class_being_emitted()
    {
        const string source = """
            using SpectraEngine.Core.Entities;

            namespace TestGame.Entities;

            [SpectraEntity("logic_thing")]
            public sealed partial class LogicThing : Entity
            {
                [Keyvalue("targetname")]
                public string Who { get; set; } = "";

                [Keyvalue("speed", Default = "1")]
                public float Speed { get; set; }
            }
            """;

        GeneratorRun run = GeneratorHarness.Run(source);

        run.DiagnosticIds.ShouldContain("SPE005");

        string generated = run.OnlySource();
        generated.ShouldContain("\"speed\"");
        generated.ShouldNotContain("targetname");
    }
}
