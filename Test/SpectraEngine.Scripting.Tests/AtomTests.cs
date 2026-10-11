using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The numbers a host gives the names of its members. A host function then
/// switches on a number where it would compare a name.
/// </summary>
public sealed class AtomTests
{
    private const int ThingTag = 1;

    [Fact]
    public void A_name_the_host_listed_reads_as_its_place_in_the_list()
    {
        using LuauHost host = OpenWithAtoms("Name\0Position\0Open\0"u8, 3);

        Atoms(host, "atom_of('Name'), atom_of('Position'), atom_of('Open')").ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void A_name_the_host_did_not_list_and_a_name_that_only_starts_like_one_read_as_none()
    {
        using LuauHost host = OpenWithAtoms("Name\0Position\0"u8, 2);

        Atoms(host, "atom_of('Size'), atom_of('Nam'), atom_of('Names'), atom_of('name'), atom_of('')")
            .ShouldBe([-1, -1, -1, -1, -1]);
    }

    [Fact]
    public void A_name_put_together_at_run_time_reads_the_same_as_one_written_out()
    {
        using LuauHost host = OpenWithAtoms("Name\0Position\0"u8, 2);

        Atoms(host, "atom_of('Posi' .. 'tion'), atom_of(string.rep('Name', 1))").ShouldBe([1, 0]);
    }

    [Fact]
    public void A_value_that_is_no_string_has_no_atom()
    {
        using LuauHost host = OpenWithAtoms("Name\0"u8, 1);

        Atoms(host, "atom_of(5), atom_of(nil), atom_of({})").ShouldBe([-2, -2, -2]);
    }

    [Fact]
    public void A_method_called_with_a_colon_arrives_as_its_atom()
    {
        using LuauHost host = OpenWithAtoms("Name\0Position\0Open\0"u8, 3);
        LuauScript script = host.LoadText("local thing = ...\nreturn thing:Open(), thing:Name(), thing:Close(), thing:Open()");

        script.Start([LuauValue.Object(ThingTag, new object())]).ShouldBe(LuauResume.Finished);

        Read(script, 4).ShouldBe([2, 0, -1, 2]);
    }

    [Fact]
    public void A_method_with_no_atom_still_has_its_name()
    {
        string? called = null;
        using LuauHost host = OpenWithAtoms("Open\0"u8, 1, stack => called = stack.NamecallName);
        LuauScript script = host.LoadText("local thing = ...\nreturn thing:Close()");

        script.Start([LuauValue.Object(ThingTag, new object())]).ShouldBe(LuauResume.Finished);

        called.ShouldBe("Close");
    }

    [Theory]
    [InlineData("Name\0Name\0", 2, "a name is in it twice")]
    [InlineData("Name\0Position", 2, "the last name does not end with a zero")]
    [InlineData("Name\0", 2, "it holds fewer names than it says")]
    [InlineData("Name\0Position\0", 1, "it holds more names than it says")]
    public void A_list_that_is_wrong_is_refused(string names, int count, string because)
    {
        NativeLuau.Require();
        using LuauHost host = new(new RecordingSink());

        Should.Throw<InvalidOperationException>(
            () => host.SetAtoms(System.Text.Encoding.UTF8.GetBytes(names), count), because);
    }

    [Fact]
    public void A_host_takes_one_list()
    {
        using LuauHost host = OpenWithAtoms("Name\0"u8, 1);

        Should.Throw<InvalidOperationException>(() => host.SetAtoms("Other\0"u8, 1));
    }

    // A host with these atoms, a global atom_of(value) that answers -2 for
    // what is no string, and objects whose every method returns its atom.
    private static LuauHost OpenWithAtoms(ReadOnlySpan<byte> names, int count, Action<LuauStack>? heard = null)
    {
        byte[] list = names.ToArray();
        return TestHost.Open(out _, fresh =>
        {
            fresh.SetAtoms(list, count);
            fresh.SetGlobal("atom_of"u8, call =>
                call.Push(call.TryGetAtom(1, out int atom) ? atom : -2) ? 1 : LuauStack.Raise);

            LuauStack stack = fresh.Stack;
            stack.NewTable().ShouldBeTrue();
            fresh.PushFunction(stack, "__namecall"u8, call =>
            {
                heard?.Invoke(call);
                return call.Push(call.NamecallAtom) ? 1 : LuauStack.Raise;
            }).ShouldBeTrue();
            stack.SetField(-2, "__namecall"u8).ShouldBeTrue();
            fresh.Objects.SetMetatable(stack, ThingTag).ShouldBeTrue();
        });
    }

    private static int[] Atoms(LuauHost host, string expressions)
    {
        LuauScript script = host.LoadText($"return {expressions}");
        script.Start().ShouldBe(LuauResume.Finished);
        return Read(script, expressions.Split("atom_of").Length - 1);
    }

    private static int[] Read(LuauScript script, int count)
    {
        int[] atoms = new int[count];
        for (int i = 0; i < count; i++)
        {
            script.Thread.Values.TryGetNumber(i + 1, out double atom).ShouldBeTrue();
            atoms[i] = (int)atom;
        }

        return atoms;
    }
}
