using Spectra.Kitchen.Scripts;
using System;

namespace Spectra.Kitchen.Tests;

/// <summary>The check of a cook whose host installed none.</summary>
public class NoScriptSyntaxCheckTests
{
    [Fact]
    public void It_says_why_it_checks_nothing()
    {
        NoScriptSyntaxCheck.Instance.Problem.ShouldContain("scook");
        NoScriptSyntaxCheck.Instance.CompilerVersion.ShouldBe("none");
    }

    [Fact]
    public void It_refuses_to_judge_a_script()
    {
        NoScriptSyntaxCheck none = NoScriptSyntaxCheck.Instance;

        // Null would be the answer of a script that compiles.
        Should.Throw<InvalidOperationException>(() => none.Check("print(1)\n"u8))
            .Message.ShouldBe(none.Problem);
    }
}
