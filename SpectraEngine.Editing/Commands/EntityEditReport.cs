namespace SpectraEngine.Editing.Commands;

/// <summary>
/// What a Make entity or a Remove entity did, or why it did nothing.
/// </summary>
/// <param name="Message">One line for a status bar or an output log.</param>
/// <param name="Applied">False when the scene was left as it was.</param>
public readonly record struct EntityEditReport(string Message, bool Applied)
{
    /// <summary>A report for an edit that happened.</summary>
    public static EntityEditReport Done(string message) => new(message, true);

    /// <summary>A report for an edit nothing acted on. The message names the way out.</summary>
    public static EntityEditReport RefusedBecause(string message) => new(message, false);
}
