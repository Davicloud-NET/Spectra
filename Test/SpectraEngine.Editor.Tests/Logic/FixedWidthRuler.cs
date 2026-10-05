using SpectraEngine.Editor.Shell.Logic;

namespace SpectraEngine.Editor.Tests.Logic;

// A ruler for layout tests: every character is as wide as the next.
internal sealed class FixedWidthRuler : ILogicTextMeasure
{
    public const double CharacterWidth = 7;

    public double Width(string text, LogicTextStyle style) => text.Length * CharacterWidth;
}
