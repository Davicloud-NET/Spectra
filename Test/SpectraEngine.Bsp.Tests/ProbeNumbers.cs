namespace SpectraEngine.Bsp.Tests;

// The seeded numbers the CFrame probe is built from. Roblox Studio's answers
// are for these numbers, so they must come out the same on every runtime.
// Not System.Random: its sequence for a seed is not promised to stay.
internal sealed class ProbeNumbers(uint seed)
{
    private uint _state = seed;

    // The next number in a range, rounded to three decimals so it reads the same in Luau.
    public float Next(float low, float high)
    {
        // The generator from Numerical Recipes. The top 24 bits are the good ones.
        _state = unchecked((_state * 1664525u) + 1013904223u);
        double unit = (_state >> 8) / 16777216.0;
        return (float)(Math.Round((low + (unit * (high - low))) * 1000.0) / 1000.0);
    }
}
