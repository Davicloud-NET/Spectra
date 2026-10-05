using SpectraEngine.Core.Entities;
using System;
using System.Threading.Tasks;

namespace SpectraEngine.Entities.Tests;

public sealed class EntityGeneratorSnapshotTests
{
    public static TheoryData<EntityPlacement> EveryPlacement => new(Enum.GetValues<EntityPlacement>());

    // Every member of the enum: the generator keeps its own table of them and
    // cannot see Core's.
    [Theory]
    [MemberData(nameof(EveryPlacement))]
    public void A_class_is_emitted_with_the_placement_it_declares(EntityPlacement placement)
    {
        string source = $$"""
            using SpectraEngine.Core.Entities;

            namespace TestGame.Entities;

            [SpectraEntity("test_thing", Placement = EntityPlacement.{{placement}})]
            public sealed partial class TestThing : Entity
            {
            }
            """;

        GeneratorRun run = GeneratorHarness.Run(source);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();
        run.OnlySource().ShouldContain($"EntityPlacement.{placement},");
    }

    [Fact]
    public Task Generated_source_for_a_representative_entity_matches_snapshot()
    {
        GeneratorRun run = GeneratorHarness.Run(Fixtures.RepresentativeEntity);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        return Verify(run.OnlySource());
    }

    [Fact]
    public void The_generated_source_compiles()
    {
        GeneratorRun run = GeneratorHarness.Run(Fixtures.RepresentativeEntity);

        run.CompileErrors().ShouldBeEmpty();
    }

    [Fact]
    public void A_distance_is_read_as_a_float_and_carries_its_bounds_into_the_schema()
    {
        const string source = """
            using SpectraEngine.Core.Entities;

            namespace TestGame.Entities;

            [SpectraEntity("env_beacon")]
            public sealed partial class EnvBeacon : Entity
            {
                [Keyvalue("reach", Default = "12", Type = KeyvalueType.Distance, Min = 0f, Max = 500f)]
                public float Reach { get; set; } = 12f;
            }
            """;

        GeneratorRun run = GeneratorHarness.Run(source);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();

        // Without the layout, so the lines of one descriptor can be read as one.
        string generated = string.Concat(run.OnlySource().Where(c => !char.IsWhiteSpace(c)));
        generated.ShouldContain("KeyvalueWire.TryParseFloat(value,outfloatparsed)");
        generated.ShouldContain("KeyvalueType.Distance,(byte)0,0f,500f,");
    }

    [Fact]
    public void An_entity_with_no_keyvalues_and_no_inputs_still_gets_a_schema_and_a_registration()
    {
        const string source = """
            using SpectraEngine.Core.Entities;

            namespace TestGame.Entities;

            [SpectraEntity("logic_auto")]
            public sealed partial class LogicAuto : Entity
            {
            }
            """;

        GeneratorRun run = GeneratorHarness.Run(source);

        run.Diagnostics.ShouldBeEmpty(run.Describe());
        run.CompileErrors().ShouldBeEmpty();

        string generated = run.OnlySource();
        generated.ShouldContain("CreateSpectraSchema");
        generated.ShouldContain("ModuleInitializer");
        generated.ShouldNotContain("ParseKeyValue");
        generated.ShouldNotContain("AcceptInput");
    }
}
