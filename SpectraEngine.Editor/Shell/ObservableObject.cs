using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// <see cref="INotifyPropertyChanged"/> base that notifies only when a value changed.
/// </summary>
// Keep the equality guard: snapshots reapply state about 30 times a second,
// and without it every reapply notifies for the whole scene.
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Assigns <paramref name="value"/> and raises a change for the calling
    /// property, unless the value is already equal. Returns whether it changed.
    /// </summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    /// <summary>Raises a change for a property whose value is computed.</summary>
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Raises a change with arguments the caller keeps, for a property that
    /// changes on every snapshot.
    /// </summary>
    protected void Raise(PropertyChangedEventArgs change) => PropertyChanged?.Invoke(this, change);
}
