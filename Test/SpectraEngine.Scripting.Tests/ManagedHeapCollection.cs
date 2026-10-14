namespace SpectraEngine.Scripting.Tests;

// Tests that weigh the managed heap, which other tests would add to while they run.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ManagedHeapCollection
{
    public const string Name = "Managed heap";
}
