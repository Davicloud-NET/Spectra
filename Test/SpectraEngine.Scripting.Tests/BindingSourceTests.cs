using System.Text.RegularExpressions;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Reads the binding's sources. A Luau call that raises jumps over the managed
/// frame that made it, which the JIT forgives and a native build does not, so
/// no test that runs the code would catch one being added.
/// </summary>
public sealed partial class BindingSourceTests
{
    // Calls on Luau itself that were read in external/luau and cannot raise.
    // No push is among them: Luau raises from a push once one frame holds
    // 8000 values. lua_settop raises the same way when it grows the stack, so
    // Lua keeps it private and only pops with it.
    private static readonly HashSet<string> CannotRaise =
    [
        "lua_gettop", "lua_settop",
        "lua_type", "lua_tonumberx", "lua_toboolean", "lua_tolstring", "lua_touserdatatagged",
        "lua_gc",
    ];

    [GeneratedRegex(@"\[LibraryImport\b[^;]*?\bpartial\s+\S+\s+(?<name>\w+)\s*\(", RegexOptions.Singleline)]
    private static partial Regex Import();

    [Fact]
    public void The_binding_imports_no_Luau_call_that_can_raise()
    {
        foreach ((string file, string name) in Imports())
        {
            if (name.StartsWith("sl_", StringComparison.Ordinal))
                continue;

            CannotRaise.ShouldContain(
                name,
                $"{file} imports {name} straight from Luau. If it can raise, run script code or fail, give it a " +
                "status form in native/luau/spectra_luau.cpp and import that. If it cannot, add it to the list in this test.");
        }
    }

    [Fact]
    public void Every_import_of_the_binding_is_in_its_two_native_files()
    {
        foreach ((string file, string name) in Imports())
        {
            string expected = name.StartsWith("sl_", StringComparison.Ordinal) ? "Shim.cs" : "Lua.cs";

            Path.GetFileName(file).ShouldBe(expected, $"{name} is imported in {file}");
        }
    }

    [Fact]
    public void Only_Lua_itself_reaches_lua_settop()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "SpectraEngine.Scripting.Luau", "Native", "Lua.cs"));

        source.ShouldContain("private static partial void lua_settop(");
    }

    private static List<(string File, string Name)> Imports()
    {
        string folder = Path.Combine(RepositoryRoot(), "SpectraEngine.Scripting.Luau");
        List<(string, string)> imports = [];
        foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            foreach (Match match in Import().Matches(File.ReadAllText(file)))
                imports.Add((file, match.Groups["name"].Value));
        }

        imports.ShouldNotBeEmpty("the binding's sources were not found, or the pattern no longer matches them");
        return imports;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Spectra.slnx")))
            folder = folder.Parent;

        folder.ShouldNotBeNull("no Spectra.slnx above the test binary");
        return folder.FullName;
    }
}
