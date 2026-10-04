using Xunit;

namespace SpectraEngine.Physics.Tests;

/// <summary>Serialises every test class that creates a Box3D world.</summary>
// b3GetWorldCount is process-global and some tests assert a delta across it,
// so classes running in parallel make those flaky. Any class that builds a
// world joins, even if it never reads the count.
[CollectionDefinition(Name)]
public sealed class NativeWorldCollection
{
    /// <summary>Collection name for <c>[Collection]</c>.</summary>
    public const string Name = "Box3D native world";
}
