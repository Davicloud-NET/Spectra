using SpectraEngine.Core.ConsoleSystem;
using System.Globalization;

namespace SpectraEngine.Editor.Shell;

// How a place reads in the output: short beside its line, whole in the tip.
internal static class OutputPlaceText
{
    // "door.luau:12". The folder is left to the tip: a row has no room for it.
    public static string Short(ConsolePlace place)
    {
        string name = place.File[(place.File.LastIndexOf('/') + 1)..];
        return place.Line > 0 ? name + ":" + place.Line.ToString(CultureInfo.InvariantCulture) : name;
    }

    // What a click does, with the whole path.
    public static string Tip(ConsolePlace place) => place.Line > 0
        ? $"Open {place.File} at line {place.Line.ToString(CultureInfo.InvariantCulture)}"
        : $"Open {place.File}";
}
