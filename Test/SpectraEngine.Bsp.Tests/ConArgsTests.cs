using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Globalization;

namespace SpectraEngine.Bsp.Tests;

/// <summary>What a command handler is handed: its name, its arguments and the world.</summary>
// A handler copies what it reads into locals and the test asserts afterwards.
// An assertion that failed inside the handler would only become a console line.
public sealed class ConArgsTests
{
    [Fact]
    public void Arguments_are_counted_after_the_name_and_lose_their_quotes()
    {
        string name = "";
        int count = -1;
        string first = "";
        string second = "";

        Probe("Probe a \"b c\"", (in ConArgs args) =>
        {
            name = args.Name.ToString();
            count = args.Count;
            first = args[0].ToString();
            second = args[1].ToString();
        });

        name.ShouldBe("Probe");
        count.ShouldBe(2);
        first.ShouldBe("a");
        second.ShouldBe("b c");
    }

    [Fact]
    public void An_argument_that_was_not_given_reads_as_empty()
    {
        bool pastTheEnd = false;
        bool beforeTheStart = false;

        Probe("probe a", (in ConArgs args) =>
        {
            pastTheEnd = args[1].IsEmpty;
            beforeTheStart = args[-1].IsEmpty;
        });

        pastTheEnd.ShouldBeTrue();
        beforeTheStart.ShouldBeTrue();
    }

    [Fact]
    public void The_raw_arguments_are_the_text_after_the_name_as_it_was_typed()
    {
        string raw = "";

        Probe("probe  \"a\\\"b\"   c  ", (in ConArgs args) => raw = args.RawArgs.ToString());

        raw.ShouldBe("\"a\\\"b\"   c");
    }

    [Fact]
    public void A_float_is_read_with_a_dot_on_any_culture()
    {
        // Built, not looked up: in globalization-invariant mode "de-DE" resolves
        // to a culture with a dot separator.
        var commaDecimal = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaDecimal.NumberFormat.NumberDecimalSeparator = ",";
        commaDecimal.NumberFormat.NumberGroupSeparator = ".";

        bool readDot = false;
        float value = 0f;
        bool readComma = true;
        bool readNotANumber = true;
        bool readMissing = true;

        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = commaDecimal;

            Probe("probe 1.5 1,5 NaN", (in ConArgs args) =>
            {
                readDot = args.TryGetFloat(0, out value);
                readComma = args.TryGetFloat(1, out _);
                readNotANumber = args.TryGetFloat(2, out _);
                readMissing = args.TryGetFloat(3, out _);
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        readDot.ShouldBeTrue();
        value.ShouldBe(1.5f);
        readComma.ShouldBeFalse();
        readNotANumber.ShouldBeFalse();
        readMissing.ShouldBeFalse();
    }

    [Fact]
    public void A_command_reaches_the_scene_and_the_entity_world_of_its_frame()
    {
        var scene = new Scene("Console");
        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        Scene? seenScene = null;
        EntityWorld? seenWorld = null;

        Probe(
            "probe",
            (in ConArgs args) =>
            {
                seenScene = args.Scene;
                seenWorld = args.Entities;
            },
            new ConsoleFrame(scene, world));

        seenScene.ShouldBeSameAs(scene);
        seenWorld.ShouldBeSameAs(world);
    }

    private static void Probe(string line, ConCommandHandler handler, ConsoleFrame frame = default)
    {
        var console = new SpectraConsole();
        console.Commands.Add(new ConCommand("probe", "probe", "Reads its arguments.", handler));

        console.Execute(line + "; echo done", in frame);

        // Anything else here is the handler's own failure, reported as a line.
        console.Output.Drain().Select(printed => printed.Text).ShouldBe(["done"]);
    }
}
