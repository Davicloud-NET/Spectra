using SpectraEngine.Scripting.Luau;
using Xunit;

namespace Spectra.Kitchen.Tests;

// The check scook cooks with, for a test that cooks real scripts or starts
// scook on a project that has some. The Luau library is build output and not
// committed, so on a fresh clone these tests skip. They must not pass.
internal static class RealScriptCheck
{
    public static LuauSyntaxCheck Require()
    {
        var check = new LuauSyntaxCheck();
        Assert.SkipWhen(check.Problem is not null, check.Problem ?? string.Empty);
        return check;
    }
}
