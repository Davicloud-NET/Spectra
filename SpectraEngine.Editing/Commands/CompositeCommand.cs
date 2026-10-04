using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Several commands that undo and redo as one history entry. Children run in
/// order on Do and in reverse on Undo.
/// </summary>
public sealed class CompositeCommand : IEditorCommand
{
    private readonly IEditorCommand[] _commands;

    /// <summary>Creates a composite over a copy of <paramref name="commands"/>.</summary>
    public CompositeCommand(string name, IReadOnlyList<IEditorCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(commands);

        _commands = new IEditorCommand[commands.Count];
        for (int i = 0; i < commands.Count; i++)
        {
            _commands[i] = commands[i]
                ?? throw new ArgumentException("Composite commands cannot contain nulls.", nameof(commands));
        }

        Name = name;
    }

    /// <summary>Creates a composite over the given commands.</summary>
    public CompositeCommand(string name, params IEditorCommand[] commands)
        : this(name, (IReadOnlyList<IEditorCommand>)commands)
    {
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>The number of child commands.</summary>
    public int Count => _commands.Length;

    /// <summary>The child command at <paramref name="index"/>, in Do order.</summary>
    public IEditorCommand this[int index] => _commands[index];

    /// <inheritdoc/>
    public void Do(Scene scene)
    {
        for (int i = 0; i < _commands.Length; i++)
            _commands[i].Do(scene);
    }

    /// <inheritdoc/>
    public void Undo(Scene scene)
    {
        // Reverse order: later commands may depend on earlier ones' effects.
        for (int i = _commands.Length - 1; i >= 0; i--)
            _commands[i].Undo(scene);
    }

    /// <inheritdoc/>
    // Forward RollBack, not Undo: a child may restore a node that left the scene.
    public void RollBack(Scene scene)
    {
        for (int i = _commands.Length - 1; i >= 0; i--)
            _commands[i].RollBack(scene);
    }
}
