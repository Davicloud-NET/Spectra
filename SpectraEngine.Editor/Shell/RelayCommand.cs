using System;
using System.Windows.Input;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// An <see cref="ICommand"/> over an action, always executable. For the
/// window's key bindings, which accept nothing else.
/// </summary>
public sealed class RelayCommand(Action execute) : ICommand
{
    private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => _execute();
}
