using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// One value of a member a script can set: how it prints, and when two are
/// the same.
/// </summary>
public sealed class MemberValueTests
{
    [Fact]
    public void A_value_prints_the_same_on_every_machine()
    {
        MemberValue.Of(new Vector3(0.1f, -0f, 1e20f)).ToString().ShouldBe("0.1, -0, 1E+20");
        MemberValue.Of("Door").ToString().ShouldBe("Door");
        MemberValue.Of(new CFrame(1f, 2f, 3f)).ToString().ShouldBe("1, 2, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1");
        MemberValue.None.ToString().ShouldBe("nil");
    }

    [Fact]
    public void Two_values_are_equal_when_they_are_of_one_kind_and_hold_the_same()
    {
        MemberValue.Of("Door").ShouldBe(MemberValue.Of("Door"));
        MemberValue.Of("Door").ShouldNotBe(MemberValue.Of("door"));
        MemberValue.Of(Vector3.One).ShouldNotBe(MemberValue.Of(new CFrame(Vector3.One)));
        MemberValue.Of(new CFrame(Vector3.One)).ShouldBe(MemberValue.Of(new CFrame(1f, 1f, 1f)));
    }
}
