namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>Starts a program. The one piece of opening a script that leaves the editor.</summary>
public interface IProgramStarter
{
    /// <summary>
    /// Starts <paramref name="start"/>. False when it did not start, and
    /// <paramref name="failure"/> then says why. Never throws.
    /// </summary>
    bool TryStart(ProgramStart start, out string failure);
}
