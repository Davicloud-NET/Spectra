using SpectraEngine.Core.ConsoleSystem;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The command table: one command per name, found in any case, listed in order.</summary>
public sealed class ConCommandTableTests
{
    private static ConCommand Command(string name) =>
        new(name, name, "Does nothing.", (in ConArgs _) => { });

    [Fact]
    public void A_second_command_with_the_same_name_is_refused_by_name()
    {
        var table = new ConCommandTable();
        ConCommand first = Command("noclip");
        table.Add(first);

        var refusal = Should.Throw<InvalidOperationException>(() => table.Add(Command("NoClip")));

        refusal.Message.ShouldContain("'noclip'");
        table.Commands.ShouldHaveSingleItem().ShouldBeSameAs(first);
    }

    [Fact]
    public void A_command_is_found_whatever_case_it_is_typed_in()
    {
        var table = new ConCommandTable();
        ConCommand added = Command("ent_fire");
        table.Add(added);

        table.TryGet("ent_fire", out ConCommand? exact).ShouldBeTrue();
        table.TryGet("ENT_Fire", out ConCommand? shouted).ShouldBeTrue();

        exact.ShouldBeSameAs(added);
        shouted.ShouldBeSameAs(added);
        table.TryGet("ent_fir", out _).ShouldBeFalse();
    }

    [Fact]
    public void Commands_list_in_name_order()
    {
        var table = new ConCommandTable();
        table.Add(Command("wait"));
        table.Add(Command("echo"));
        table.Add(Command("Help"));
        table.Add(Command("ent_list"));

        // Ordinal, so a capital sorts ahead of every lower-case name.
        table.Commands.Select(command => command.Name).ShouldBe(["Help", "echo", "ent_list", "wait"]);
    }
}
