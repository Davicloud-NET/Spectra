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
