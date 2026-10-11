using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Picks the row a line in the output is drawn with. A line with no place
/// gets the plain row, one that names a place a row that opens it, and an
/// error with calls under it that row and its calls.
/// </summary>
// Three templates and not one with hidden parts: a script can print a hundred
// lines a tick, and each row builds only what it shows.
public sealed class OutputRowTemplate : IDataTemplate
{
    /// <summary>The row of a line with no place.</summary>
    public IDataTemplate? Plain { get; set; }

    /// <summary>The row of a line that names a place.</summary>
    public IDataTemplate? Placed { get; set; }

    /// <summary>The row of an error with calls under it.</summary>
    public IDataTemplate? WithCalls { get; set; }

    /// <inheritdoc/>
    public bool Match(object? data) => data is OutputEntry;

    /// <inheritdoc/>
    public Control? Build(object? param)
    {
        IDataTemplate? template = param switch
        {
            OutputEntry { Stack: not null } => WithCalls,
            OutputEntry { HasPlace: true } => Placed,
            _ => Plain,
        };

        return template?.Build(param);
    }
}
