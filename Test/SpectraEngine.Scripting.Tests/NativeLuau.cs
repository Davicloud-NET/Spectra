namespace SpectraEngine.Scripting.Tests;

// The guard of every test that calls into Luau. The library is build output
// and not committed, so on a fresh clone these tests skip. They must not pass.
internal static class NativeLuau
{
    private static readonly string FileName = OperatingSystem.IsWindows() ? "spectra_luau.dll" : "libspectra_luau.so";

    internal static void Require() =>
        Assert.SkipWhen(
            !File.Exists(Path.Combine(AppContext.BaseDirectory, FileName)),
            $"{FileName} is not beside the test binary. It is build output of the external/luau " +
            "submodule. Build it with native/build-luau.ps1 on Windows or native/build-luau.sh on Linux.");
}
