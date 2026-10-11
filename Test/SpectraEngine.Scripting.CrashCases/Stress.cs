using System.Runtime.CompilerServices;

namespace SpectraEngine.Scripting.CrashCases;

// What the cases share that put the collector to work. A collection that
// walks a stack a raise has broken frees or moves what the canaries are.
internal static class Stress
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string[] MakeCanaries()
    {
        string[] canaries = new string[64];
        for (int i = 0; i < canaries.Length; i++)
            canaries[i] = "canary " + i;
        return canaries;
    }

    // Null when every canary is what it was.
    internal static string? FindDamage(string[] canaries)
    {
        for (int i = 0; i < canaries.Length; i++)
        {
            if (canaries[i] != "canary " + i)
                return $"canary {i} was damaged";
        }

        return null;
    }

    // Makes garbage and collects it, here and now.
    internal static void Churn()
    {
        object[] garbage = new object[256];
        for (int i = 0; i < 2048; i++)
            garbage[i & 255] = new byte[128 + (i & 63)];

        GC.Collect();
        GC.KeepAlive(garbage);
    }
}
