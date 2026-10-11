using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>The startup check against the library that was built.</summary>
public sealed unsafe class LuauLibraryTests
{
    [Fact]
    public void The_constants_the_binding_copied_match_the_library()
    {
        NativeLuau.Require();

        LuauLibrary.FindProblem().ShouldBeNull();
    }

    // A library left in native/runtimes from before a change to the shim has
    // every Luau constant right. Only this number tells it apart.
    [Fact]
    public void The_library_reports_the_revision_of_the_shim_it_was_built_from()
    {
        NativeLuau.Require();
        int revision = 0;

        int known = Shim.sl_constant("SL_SHIM_REVISION", &revision);

        known.ShouldBe(1);
        revision.ShouldBe(Shim.Revision);
    }

    // The check compares by name. A library that answered for any name would
    // make it pass whatever the binding asked.
    [Fact]
    public void The_library_does_not_answer_for_a_constant_it_does_not_have()
    {
        NativeLuau.Require();
        int value = 7;

        int known = Shim.sl_constant("LUA_NO_SUCH_CONSTANT", &value);

        known.ShouldBe(0);
        value.ShouldBe(7);
    }
}
