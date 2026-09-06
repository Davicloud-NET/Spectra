using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

public class CompileWorkspaceTests
{
    [Fact]
    public void Leases_are_exclusive_clear_references_and_discard_large_capacity()
    {
        var first = CompileWorkspace.Rent();
        var list = first.List<object>();
        list.Add(new object());
        using (var nested = CompileWorkspace.Rent())
        {
            nested.ShouldNotBeSameAs(first);
            nested.List<object>().ShouldNotBeSameAs(list);
        }
        first.Dispose();
        list.ShouldBeEmpty();

        var largeLease = CompileWorkspace.Rent();
        var large = largeLease.List<object>();
        large.EnsureCapacity(4097);
        large.Add(new object());
        largeLease.Dispose();
        large.ShouldBeEmpty();
        using var next = CompileWorkspace.Rent();
        next.List<object>().ShouldNotBeSameAs(large);
    }
}
