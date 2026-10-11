using System.Text.RegularExpressions;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Reads the solution's sources. A Luau call that raises jumps over the managed
/// frame that made it, which the JIT forgives and a native build does not, so
/// no test that runs the code would catch one being added.
/// </summary>
public sealed partial class BindingSourceTests
{
    private const string Binding = "SpectraEngine.Scripting.Luau";

    // Calls on Luau itself that were read in external/luau and cannot raise.
    // No push is among them: Luau raises from a push once one frame holds
    // 8000 values. lua_settop raises the same way when it grows the stack, so
    // Lua keeps it private and only pops with it.
    private static readonly HashSet<string> CannotRaise =
    [
        "lua_gettop", "lua_settop",
        "lua_type", "lua_tonumberx", "lua_toboolean", "lua_tolstring", "lua_tovector", "lua_objlen",
        "lua_touserdatatagged", "lua_tothread", "lua_status", "lua_costatus", "lua_isyieldable",
        "lua_getthreaddata", "lua_setthreaddata", "lua_unref",
        "lua_gc",
    ];

    // The files outside the binding that may name the library, each with the
    // imports it may hold: the crash case that shows what a raise does, the
    // guard that looks for the file, and this test.
    private static readonly Dictionary<string, string[]> MayNameTheLibrary = new()
    {
        ["WrongWay.cs"] = ["lua_error"],
        ["NativeLuau.cs"] = [],
        ["BindingSourceTests.cs"] = [],
    };

    // Either attribute, however it is qualified, on a partial or an extern
    // method. The name is the last word before the parameter list.
    [GeneratedRegex(@"\b(?:LibraryImport|DllImport)(?:Attribute)?\b[^;]*?\b(?:partial|extern)\b[^;(]*?(?<name>\w+)\s*\(", RegexOptions.Singleline)]
    private static partial Regex Import();

    // The library's name in a string, or the binding's constant for it.
    [GeneratedRegex(@"""[^""\n]*spectra_luau|\bLua\.Library\b")]
    private static partial Regex LibraryName();

    [Fact]
    public void The_binding_imports_no_Luau_call_that_can_raise()
    {
        foreach ((string file, string name) in BindingImports())
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
        foreach ((string file, string name) in BindingImports())
        {
            string expected = name.StartsWith("sl_", StringComparison.Ordinal) ? "Shim.cs" : "Lua.cs";

            Path.GetFileName(file).ShouldBe(expected, $"{name} is imported in {file}");
        }
    }

    [Fact]
    public void Only_Lua_itself_reaches_lua_settop()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), Binding, "Native", "Lua.cs"));

        source.ShouldContain("private static partial void lua_settop(");
    }

    // An export found at run time has no declaration for these tests to read.
    [Fact]
    public void The_binding_looks_up_no_export_by_name()
    {
        foreach (string file in Sources(Path.Combine(RepositoryRoot(), Binding)))
        {
            File.ReadAllText(file).ShouldNotContain(
                "NativeLibrary",
                Case.Sensitive,
                $"{file} uses NativeLibrary. Declare the call as an import in Lua.cs or Shim.cs, where its name can be checked.");
        }
    }

    [Fact]
    public void No_other_project_imports_from_the_Luau_library()
    {
        foreach (string project in ProjectFolders())
        {
            if (Path.GetFileName(project) == Binding)
                continue;

            foreach (string file in Sources(project))
            {
                string source = File.ReadAllText(file);
                if (!LibraryName().IsMatch(source))
                    continue;

                MayNameTheLibrary.ShouldContainKey(
                    Path.GetFileName(file),
                    $"{file} names the Luau library. Only {Binding} imports from it, so that every call is checked here.");
                ImportsIn(source).ShouldBe(MayNameTheLibrary[Path.GetFileName(file)], $"the imports of {file}");
            }
        }
    }

    private static List<(string File, string Name)> BindingImports()
    {
        List<(string, string)> imports = [];
        foreach (string file in Sources(Path.Combine(RepositoryRoot(), Binding)))
        {
            foreach (string name in ImportsIn(File.ReadAllText(file)))
                imports.Add((file, name));
        }

        imports.ShouldNotBeEmpty("the binding's sources were not found, or the pattern no longer matches them");
        return imports;
    }

    private static string[] ImportsIn(string source) =>
        [.. Import().Matches(source).Select(match => match.Groups["name"].Value)];

    private static IEnumerable<string> Sources(string folder)
    {
        char slash = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{slash}obj{slash}", StringComparison.Ordinal)
                && !file.Contains($"{slash}bin{slash}", StringComparison.Ordinal));
    }

    // The folder of every project in the solution.
    private static List<string> ProjectFolders()
    {
        string root = RepositoryRoot();
        List<string> folders = [];
        foreach (Match match in ProjectPath().Matches(File.ReadAllText(Path.Combine(root, "Spectra.slnx"))))
        {
            string project = Path.Combine(root, match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar));
            folders.Add(Path.GetDirectoryName(project) ?? root);
        }

        folders.Count.ShouldBeGreaterThan(10, "Spectra.slnx no longer lists its projects the way this test reads them");
        return folders;
    }

    [GeneratedRegex(@"<Project\s+Path=""(?<path>[^""]+\.csproj)""")]
    private static partial Regex ProjectPath();

    private static string RepositoryRoot()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Spectra.slnx")))
            folder = folder.Parent;

        folder.ShouldNotBeNull("no Spectra.slnx above the test binary");
        return folder.FullName;
    }
}
