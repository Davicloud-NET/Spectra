using SpectraEngine.Core.Inspection;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The Now section: the selected entity's state while the level runs. Empty
/// when nothing is running. UI thread only.
/// </summary>
public sealed class EntityLiveStateModel : ObservableObject
{
    /// <summary>The rows, in the order the entity wrote them.</summary>
    public ObservableCollection<EntityStateRowModel> Rows { get; } = [];

    /// <summary>Whether there is anything to show.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Takes one published snapshot's entity payload.</summary>
    public void Apply(EntityPanelInfo? info)
    {
        IReadOnlyList<KeyValuePair<string, string>> state = info?.State ?? [];
        bool countChanged = Rows.Count != state.Count;

        // Patched in place: the values change every few ticks, the rows do not.
        while (Rows.Count > state.Count)
            Rows.RemoveAt(Rows.Count - 1);

        while (Rows.Count < state.Count)
            Rows.Add(new EntityStateRowModel());

        for (int i = 0; i < state.Count; i++)
        {
            Rows[i].Name = state[i].Key;
            Rows[i].Value = state[i].Value;
        }

        if (countChanged)
            Raise(nameof(HasRows));
    }
}
