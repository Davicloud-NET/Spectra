namespace SpectraEngine.Core.Scripting;

/// <summary>Where the text of a script comes from.</summary>
public interface IScriptSource
{
    /// <summary>The script's text as UTF-8, or null when it cannot be had.</summary>
    /// <param name="path">The script's content path.</param>
    /// <param name="problem">
    /// With no text: why the file that is there cannot be read as a script,
    /// or null when there is no file at that path.
    /// </param>
    byte[]? Read(string path, out string? problem);
}
