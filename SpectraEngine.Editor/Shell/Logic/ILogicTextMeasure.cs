namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>Tells the layout how wide a text will be drawn.</summary>
public interface ILogicTextMeasure
{
    /// <summary>The width of <paramref name="text"/> in pixels, set in <paramref name="style"/>.</summary>
    double Width(string text, LogicTextStyle style);
}
