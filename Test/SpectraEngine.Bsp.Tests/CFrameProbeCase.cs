namespace SpectraEngine.Bsp.Tests;

/// <summary>One named case of the CFrame probe: what Roblox is asked and what the engine answers.</summary>
/// <param name="Name">The key of the case in the probe's output.</param>
/// <param name="Reader">The probe function that turns the result into numbers: frame, vector or angles.</param>
/// <param name="Luau">The Luau expression Roblox evaluates.</param>
/// <param name="Ours">The engine's answer: twelve components, or three numbers.</param>
/// <param name="IsOurChoice">
/// True where Roblox's documentation gives no answer and the engine chose one.
/// </param>
internal sealed record CFrameProbeCase(string Name, string Reader, string Luau, float[] Ours, bool IsOurChoice)
{
    public const string Frame = "frame";
    public const string Vector = "vector";
    public const string Angles = "angles";
}
