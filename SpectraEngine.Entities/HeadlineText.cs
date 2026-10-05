using System.Globalization;

namespace SpectraEngine.Entities;

// The wording the built-in classes share in their headlines.
internal static class HeadlineText
{
    public static string YesNo(bool value) => value ? "yes" : "no";

    public static string Times(int count) =>
        count == 1 ? "1 time" : string.Create(CultureInfo.InvariantCulture, $"{count} times");

    public static string TicksOf(int done, int total) =>
        string.Create(CultureInfo.InvariantCulture, $"{done} of {total} ticks");

    public static string Number(float value) => value.ToString(CultureInfo.InvariantCulture);

    public static string NumberOf(float value, float most) =>
        string.Create(CultureInfo.InvariantCulture, $"{value} of {most}");
}
