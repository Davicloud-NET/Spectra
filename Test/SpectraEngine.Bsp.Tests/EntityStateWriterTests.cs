using System.Globalization;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>How an entity describes its live state, and how the values are written.</summary>
public sealed class EntityStateWriterTests
{
    [Fact]
    public void An_entity_describes_its_state_in_the_order_it_writes_it()
    {
        var scene = new Scene("State");
        EntityRuntime.Place(scene.Root, "counter", "counting");
        var catalog = new EntityCatalog();
        catalog.Add(new EntitySchema("counting"), () => new CountingEntity());
        var world = new EntityWorld(scene, new CapturingLogger(), catalog);
        world.Activate();

        world.QueueInput("counter", "Add");
        world.Tick(1f / 60f);

        Describe(world.Entities[0]).ShouldBe(new[]
        {
            Row("enabled", "1"),
            Row("count", "1"),
            Row("last input", "Add"),
        });
    }

    [Fact]
    public void A_class_that_does_not_describe_itself_has_no_state()
    {
        var scene = new Scene("State");
        EntityRuntime.Place(scene.Root, "relay", "relay");
        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        Describe(world.Entities[0]).ShouldBeEmpty();
    }

    [Fact]
    public void Numbers_are_written_the_same_whatever_the_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            // German writes one half as 0,5.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var rows = new List<KeyValuePair<string, string>>();
            var state = new EntityStateWriter(rows);
            state.Add("delay", 0.5f);
            state.Add("count", -1200);

            rows.ShouldBe(new[] { Row("delay", "0.5"), Row("count", "-1200") });
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_float_that_is_not_finite_is_written_out()
    {
        var rows = new List<KeyValuePair<string, string>>();
        var state = new EntityStateWriter(rows);

        state.Add("next think", float.PositiveInfinity);
        state.Add("speed", float.NaN);

        rows.ShouldBe(new[] { Row("next think", "Infinity"), Row("speed", "NaN") });
    }

    private static List<KeyValuePair<string, string>> Describe(Entity entity)
    {
        var rows = new List<KeyValuePair<string, string>>();
        entity.DescribeState(new EntityStateWriter(rows));
        return rows;
    }

    private static KeyValuePair<string, string> Row(string name, string value) => new(name, value);

    private sealed class CountingEntity : Entity
    {
        private int _count;
        private string _lastInput = "";

        public override bool AcceptInput(string input, ref EntityInputContext context)
        {
            _count++;
            _lastInput = input;
            return true;
        }

        public override void DescribeState(EntityStateWriter state)
        {
            state.Add("enabled", true);
            state.Add("count", _count);
            state.Add("last input", _lastInput);
        }
    }
}
