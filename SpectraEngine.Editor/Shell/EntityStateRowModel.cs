namespace SpectraEngine.Editor.Shell;

/// <summary>One named value of a running entity's state.</summary>
public sealed class EntityStateRowModel : ObservableObject
{
    private string _name = "";
    private string _value = "";

    /// <summary>What the value is.</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>The value, as the entity wrote it.</summary>
    public string Value
    {
        get => _value;
        set => Set(ref _value, value);
    }
}
