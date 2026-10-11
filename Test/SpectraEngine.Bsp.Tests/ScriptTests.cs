using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The script payload as a value: its path, its two switches, and what an edit of it is.</summary>
public sealed class ScriptTests
{
    [Fact]
    public void A_new_script_is_switched_on_and_is_no_module()
    {
        var script = new Script("Scripts/door.luau");

        script.Path.ShouldBe("Scripts/door.luau");
        script.IsEnabled.ShouldBeTrue();
        script.IsModule.ShouldBeFalse();
    }

    [Theory]
    [InlineData(@"Scripts\door.luau")]
    [InlineData("/Scripts/door.luau")]
    [InlineData("Scripts/./door.luau")]
    [InlineData(" Scripts / door.luau ")]
    public void A_script_holds_its_file_as_the_path_every_asset_is_known_by(string written)
    {
        new Script(written).Path.ShouldBe("Scripts/door.luau");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../door.luau")]
    [InlineData(@"C:\Game\Assets\Scripts\door.luau")]
    public void A_script_that_names_no_file_under_the_content_root_cannot_be_made(string path)
    {
        Should.Throw<ArgumentException>(() => new Script(path));
        Should.Throw<ArgumentException>(() => new Script("Scripts/door.luau") with { Path = path });
    }

    [Fact]
    public void An_edit_is_a_new_script_and_the_old_one_stays_as_it_was()
    {
        var before = new Script("Scripts/door.luau");

        Script after = before with { IsEnabled = false, IsModule = true };

        after.ShouldNotBeSameAs(before);
        after.IsEnabled.ShouldBeFalse();
        after.IsModule.ShouldBeTrue();
        after.Path.ShouldBe("Scripts/door.luau");
        before.IsEnabled.ShouldBeTrue();
        before.IsModule.ShouldBeFalse();
    }

    [Fact]
    public void A_new_path_is_normalized_like_the_first_one()
    {
        Script moved = new Script("Scripts/door.luau") with { Path = @"Scripts\Doors\vault.luau" };

        moved.Path.ShouldBe("Scripts/Doors/vault.luau");
    }

    [Fact]
    public void Two_scripts_that_say_the_same_are_equal_and_any_difference_tells_them_apart()
    {
        var one = new Script("Scripts/door.luau");

        new Script(@"Scripts\door.luau").ShouldBe(one);
        (one with { IsEnabled = false }).ShouldNotBe(one);
        (one with { IsModule = true }).ShouldNotBe(one);
        new Script("Scripts/gate.luau").ShouldNotBe(one);
    }
}
