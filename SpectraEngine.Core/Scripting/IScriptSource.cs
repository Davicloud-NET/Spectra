namespace SpectraEngine.Core.Scripting;

/// <summary>Where the text of a script comes from.</summary>
public interface IScriptSource
{
    /// <summary>
    /// The script's text as UTF-8, or null when there is no file at that
    /// content path or what is there cannot be read as a script.
    /// </summary>
    byte[]? Read(string path);
}
