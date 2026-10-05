namespace SpectraEngine.Bsp.Tests;

/// <summary>Serialises every test class that opens OpenAL's loopback device.</summary>
// The current OpenAL context is one per process.
// Not run beside other collections either: on Linux the tests that count a
// thread's allocations failed now and then with these running next to them.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AudioLoopbackCollection
{
    /// <summary>Collection name for <c>[Collection]</c>.</summary>
    public const string Name = "OpenAL loopback";

    /// <summary>The environment variable that turns these tests on when it is 1.</summary>
    public const string Switch = "SPECTRA_AUDIO_LOOPBACK";

    /// <summary>Skips the calling test unless the switch is set.</summary>
    public static void Require() =>
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(Switch) == "1",
            $"Opt-in: set {Switch}=1 to run the tests that render through the real OpenAL Soft. Nothing is played.");
}
