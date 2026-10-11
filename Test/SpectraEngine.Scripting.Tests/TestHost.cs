using System.Text;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

// Short ways to a sealed host and a loaded script, for the tests of the binding.
internal static class TestHost
{
    internal const string ScriptName = "Scripts/test.luau";

    // The setup runs before the host is sealed, which is when globals can be set.
    internal static LuauHost Open(out RecordingSink sink, Action<LuauHost>? setup = null)
    {
        NativeLuau.Require();
        sink = new RecordingSink();
        LuauHost host = new(sink);
        setup?.Invoke(host);
        host.Seal();
        return host;
    }

    internal static void SetGlobal(this LuauHost host, ReadOnlySpan<byte> name, LuauHostFunction function)
    {
        host.PushFunction(host.Stack, name, function).ShouldBeTrue();
        host.Stack.SetGlobal(name).ShouldBeTrue();
    }

    internal static LuauScript LoadText(this LuauHost host, string source, string name = ScriptName)
    {
        LuauScript? script = host.Load(name, Encoding.UTF8.GetBytes(source));
        script.ShouldNotBeNull("the script did not compile");
        return script;
    }

    internal static LuauResume RunText(this LuauHost host, string source, string name = ScriptName) =>
        host.LoadText(source, name).Start();
}
