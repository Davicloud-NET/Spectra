using Xunit.Sdk;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The guard of the tests that need the Luau library: it only lets a test
/// through that CI runs with a skip counted as a failure.
/// </summary>
public class RealScriptCheckTests
{
    [Fact]
    public void A_test_without_the_trait_fails_and_is_told_to_carry_it()
    {
        // This test has no trait. With Luau missing it would skip, and no
        // step would notice.
        Should.Throw<TrueException>(() => RealScriptCheck.Require())
            .Message.ShouldContain("RealScriptCheck.Suite");
    }

    [Fact]
    [Trait("Suite", RealScriptCheck.Suite)]
    public void A_test_with_the_trait_gets_the_check_or_skips()
    {
        RealScriptCheck.Require().Problem.ShouldBeNull();
    }
}
