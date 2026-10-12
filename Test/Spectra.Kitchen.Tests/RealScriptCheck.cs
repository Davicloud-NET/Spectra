using SpectraEngine.Scripting.Luau;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Spectra.Kitchen.Tests;

// The check scook cooks with, for a test that cooks real scripts or starts
// scook on a project that has some. The Luau library is build output and not
// committed, so on a fresh clone these tests skip. They must not pass.
internal static class RealScriptCheck
{
    // The trait of every test that calls Require. CI runs the tests that
    // have it in a step where a skip fails.
    public const string Suite = "RealLuau";

    public static LuauSyntaxCheck Require()
    {
        RequireTheTrait();

        var check = new LuauSyntaxCheck();
        Assert.SkipWhen(check.Problem is not null, check.Problem ?? string.Empty);
        return check;
    }

    // A failure and not a skip: a test without the trait is in no step that
    // counts skips, and a missing library would go unseen there.
    private static void RequireTheTrait()
    {
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? traits = TestContext.Current.TestCase?.Traits;

        bool has = traits is not null
            && traits.TryGetValue("Suite", out IReadOnlyCollection<string>? suites)
            && suites.Contains(Suite);

        Assert.True(
            has,
            $"This test needs the Luau library, so it carries [Trait(\"Suite\", RealScriptCheck.Suite)], on " +
            "itself or on its class.");
    }
}
