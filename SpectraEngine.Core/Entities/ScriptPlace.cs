using System;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Which script did something in a running level, and where in its code: the
/// script's node, and the file and the line. The file is a module's when a
/// module's code did it for the script.
/// </summary>
public readonly record struct ScriptPlace
{
    private readonly string? _file;

    /// <param name="node">The script's node, or empty when it is not known.</param>
    /// <param name="file">The file the line is in, as a content path. Empty when it is not known.</param>
    /// <param name="line">The line, or 0 when it is not known.</param>
    public ScriptPlace(Guid node, string file, int line)
    {
        Node = node;
        _file = string.IsNullOrEmpty(file) ? null : file;
        Line = line;
    }

    /// <summary>The script's node. Empty when it is not known, as for what a host did.</summary>
    public Guid Node { get; }

    /// <summary>The file the line is in, as a content path. Empty when it is not known.</summary>
    public string File => _file ?? "";

    /// <summary>The line in <see cref="File"/>, or 0 when it is not known.</summary>
    public int Line { get; }

    /// <summary>Whether the script's node is known.</summary>
    public bool HasNode => Node != Guid.Empty;
}
