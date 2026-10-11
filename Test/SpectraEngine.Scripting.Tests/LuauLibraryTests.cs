using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>The startup check against the library that was built.</summary>
public sealed class LuauLibraryTests
{
    [Fact]
    public void The_constants_the_binding_copied_match_the_library()
    {
        NativeLuau.Require();

        LuauLibrary.FindProblem().ShouldBeNull();
    }
}
