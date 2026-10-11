namespace SpectraEngine.Bsp.Tests;

// One named case of the CFrame probe: what Roblox is asked and what the engine
// answers. Reader is the probe function that turns the result into numbers,
// and Ours holds twelve components or three numbers. IsOurChoice is true
// where Roblox's documentation gives no answer and the engine chose one.
internal sealed record CFrameProbeCase(string Name, string Reader, string Luau, float[] Ours, bool IsOurChoice)
{
    public const string Frame = "frame";
    public const string Vector = "vector";
    public const string Angles = "angles";
}
