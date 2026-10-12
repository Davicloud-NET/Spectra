using System.Diagnostics.CodeAnalysis;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Turns the string a script hands require into the files it may name, by
// Luau's rules. A module is a file x.luau or a folder's x/init.luau, and a
// path names it without the extension. Nothing here reads a file.
internal static class ModulePath
{
    // What a path has to look like. Every refusal of one ends with it.
    public const string Shape = "A path starts with ./ or ../, from the file that asks, or with @self or @assets";

    private const string Extension = PackedScript.FileExtension;
    private const string Init = "init";

    // The files the string may name, as content paths: x.luau, then
    // x/init.luau. False with the reason when it names none.
    public static bool TryResolve(
        string asked, string asker, out string[] files, [NotNullWhen(false)] out string? refusal)
    {
        files = [];
        List<string> place = [];

        refusal = Start(asked, asker, place, out string rest) ?? Walk(asked, rest, place);
        return refusal is null && TryName(asked, place, out files, out refusal);
    }

    public static string Missing(string asked, string[] files) =>
        $"no module at '{asked}': looked for {string.Join(" and ", files)}{Hint(asked)}";

    public static string TwoFiles(string asked, string first, string second) =>
        $"'{asked}' names two files, {first} and {second}. Remove one";

    public static string Unreadable(string asked, string file, string problem) =>
        $"'{asked}' is {file}, which cannot be read. {problem}";

    // Where the walk begins: beside the module that asks, at it, or at the
    // content root. What is left to walk comes back in `rest`.
    private static string? Start(string asked, string asker, List<string> place, out string rest)
    {
        rest = asked.Replace('\\', '/');
        if (rest.StartsWith("./", StringComparison.Ordinal) || rest.StartsWith("../", StringComparison.Ordinal))
        {
            // An init file is its folder, so its ./ is beside the folder.
            if (!TryAddModule(asker, place))
                return NoAsker(asked);
            if (place.Count == 0)
                return Climbs(asked);

            place.RemoveAt(place.Count - 1);
            return null;
        }

        if (!rest.StartsWith('@'))
            return $"'{asked}' is {(IsAbsolute(rest) ? "an absolute path" : "no path to a module")}. {Shape}";

        int slash = rest.IndexOf('/', StringComparison.Ordinal);
        string alias = slash < 0 ? rest[1..] : rest[1..slash];
        rest = slash < 0 ? string.Empty : rest[(slash + 1)..];

        if (alias.Equals("self", StringComparison.OrdinalIgnoreCase))
            return TryAddModule(asker, place) ? null : NoAsker(asked);

        return alias.Equals("assets", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"'@{alias}' is not a valid alias. {Shape}";
    }

    private static string? Walk(string asked, string rest, List<string> place)
    {
        foreach (string name in rest.Split('/'))
        {
            if (name.Length == 0 || name == ".")
                continue;

            if (name != "..")
                place.Add(name);
            else if (place.Count == 0)
                return Climbs(asked);
            else
                place.RemoveAt(place.Count - 1);
        }

        return null;
    }

    // The content root has no name and init is a folder's own file, so
    // neither can be x.luau.
    private static bool TryName(
        string asked, List<string> place, out string[] files, [NotNullWhen(false)] out string? refusal)
    {
        string module = string.Join('/', place);
        string init = place.Count == 0 ? Init + Extension : $"{module}/{Init}{Extension}";
        bool isFolderOnly = place.Count == 0 || place[^1].Equals(Init, StringComparison.OrdinalIgnoreCase);

        try
        {
            init = ContentRoot.NormalizeRelativePath(init);
            files = isFolderOnly ? [init] : [ContentRoot.NormalizeRelativePath(module + Extension), init];
            refusal = null;
            return true;
        }
        catch (ArgumentException)
        {
            files = [];
            refusal = $"'{asked}' names no file under the content root. {Shape}";
            return false;
        }
    }

    // The module a file is, as the names from the content root down to it:
    // the file without its extension, or the folder of an init file.
    private static bool TryAddModule(string file, List<string> place)
    {
        if (file.Length == 0)
            return false;

        bool hasExtension = file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
        string module = hasExtension ? file[..^Extension.Length] : file;
        place.AddRange(module.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (place.Count > 0 && place[^1].Equals(Init, StringComparison.OrdinalIgnoreCase))
            place.RemoveAt(place.Count - 1);

        return true;
    }

    private static bool IsAbsolute(string path) =>
        path.StartsWith('/') || (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]));

    private static string Climbs(string asked) => $"'{asked}' climbs out of the content root. {Shape}";

    private static string NoAsker(string asked) =>
        $"'{asked}' is found from the file that asks, and this code has none. Start the path with @assets";

    private static string Hint(string asked)
    {
        if (asked.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            || asked.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            return ". A path leaves the extension out";
        }

        return asked.EndsWith("/" + Init, StringComparison.OrdinalIgnoreCase)
            ? ". An init file is asked for by its folder's name"
            : string.Empty;
    }
}
