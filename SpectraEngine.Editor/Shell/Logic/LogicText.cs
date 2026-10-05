using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>The sentences the Logic view's graph carries, in one place.</summary>
public static class LogicText
{
    /// <summary>The name on the card every <c>!activator</c> wire ends at.</summary>
    public const string ActivatorName = "The activator";

    /// <summary>The line under <see cref="ActivatorName"/>.</summary>
    public const string ActivatorLine = "Decided while playing";

    /// <summary>The line on the card of a name nothing has.</summary>
    public const string MissingNameLine = "No entity has this name";

    /// <summary>The line on the card of a prefix nothing starts with.</summary>
    public const string MissingPrefixLine = "No entity matches this";

    /// <summary>The line on the card of a wire with an empty target.</summary>
    public const string NoTargetLine = "No target set";

    /// <summary>The line a stub card shows where an entity shows its class.</summary>
    public static string StubLine(LogicStubKind stub) => stub switch
    {
        LogicStubKind.Activator => ActivatorLine,
        LogicStubKind.MissingName => MissingNameLine,
        LogicStubKind.MissingPrefix => MissingPrefixLine,
        LogicStubKind.NoTarget => NoTargetLine,
        _ => "",
    };

    /// <summary>
    /// The note under a card's ports about the outputs it does not list, or
    /// empty when it lists them all.
    /// </summary>
    public static string OutputsNote(int declared, int wired)
    {
        int hidden = declared - wired;
        if (hidden <= 0)
            return "";

        if (wired == 0)
            return declared == 1 ? "1 output, not wired" : $"{Number(declared)} outputs, none wired";

        return hidden == 1 ? "1 more output" : $"{Number(hidden)} more outputs";
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
