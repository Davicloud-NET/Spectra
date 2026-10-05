using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SoundCornersSpike.Questions;

namespace SoundCornersSpike;

internal static class Program
{
    private static readonly (string Name, Action Run)[] Sections =
    [
        ("cells", Cells.Run),
        ("accuracy", Accuracy.Run),
        ("cost", Cost.Run),
        ("outdoors", Outdoors.Run),
        ("direction", Direction.Run),
        ("staleness", Staleness.Run),
        ("sounds", Sounds.Run),
        ("doors", Doors.Run),
        ("breaks", Breaks.Run),
        ("threads", Threads.Run),
    ];

    // Only run when asked for by name.
    private static readonly (string Name, Action Run)[] Extras =
    [
        ("dump", PathDump.Run),
    ];

    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var wanted = new List<string>();
        foreach (string arg in args)
        {
            if (arg.StartsWith("--scale=", StringComparison.Ordinal))
                Timing.Scale = double.Parse(arg["--scale=".Length..], CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--chosen=", StringComparison.Ordinal))
                Accuracy.Chosen = Accuracy.Methods[int.Parse(arg["--chosen=".Length..], CultureInfo.InvariantCulture)];
            else if (arg == "--no-pin")
                Pinning.Enabled = false;
            else
                wanted.Add(arg);
        }

        Pinning.Apply();
        PrintRig();

        foreach ((string name, Action run) in Sections)
        {
            if (wanted.Count > 0 && !wanted.Contains(name)) continue;

            long start = Stopwatch.GetTimestamp();
            run();
            Console.WriteLine($"INFO section '{name}' took {Stopwatch.GetElapsedTime(start).TotalSeconds:F1} s");
        }

        foreach ((string name, Action run) in Extras)
        {
            if (wanted.Contains(name)) run();
        }

        return 0;
    }

    private static void PrintRig()
    {
        Console.WriteLine($"INFO machine: {Pinning.ProcessorName()}, {Environment.ProcessorCount} logical CPUs");
        Console.WriteLine($"INFO system: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"INFO runtime: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"INFO compiled ahead of time: {!RuntimeFeature.IsDynamicCodeCompiled}");
        Console.WriteLine($"INFO pinned: {Pinning.Describe()}");
        Console.WriteLine($"INFO timing scale: {Timing.Scale}");
        Console.WriteLine($"INFO chosen flood: {Accuracy.Chosen.Name}");
    }
}
