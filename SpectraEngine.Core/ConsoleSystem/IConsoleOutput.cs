namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>Where a command writes its reply, one line a call.</summary>
public interface IConsoleOutput
{
    /// <summary>Writes a line.</summary>
    void Print(string text);

    /// <summary>Writes a line about something the user should look at.</summary>
    void Warn(string text);

    /// <summary>Writes a line about something that was not done.</summary>
    void Error(string text);

    /// <summary>
    /// Writes a line with all it carries: its place, the calls under an error
    /// and the node it is a problem of. Its number is given here, whatever
    /// the line holds.
    /// </summary>
    void Write(in ConsoleLine line);
}
