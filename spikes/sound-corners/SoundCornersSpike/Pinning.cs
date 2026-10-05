using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace SoundCornersSpike;

// Keeps the timings on one core at a raised priority. The machine has fast
// and slow cores, and a run that wanders between them is two runs.
internal static class Pinning
{
    // Logical CPU 4: a fast core on the machine this was written on.
    private const int Core = 4;

    private static bool _applied;

    public static bool Enabled { get; set; } = true;

    public static void Apply()
    {
        if (!Enabled || !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux())) return;
        if (Environment.ProcessorCount <= Core) return;

        using Process process = Process.GetCurrentProcess();
        process.ProcessorAffinity = 1 << Core;
        process.PriorityClass = ProcessPriorityClass.High;
        _applied = true;
    }

    public static void Release()
    {
        if (!_applied || !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux())) return;

        using Process process = Process.GetCurrentProcess();
        process.ProcessorAffinity = (nint)((1L << Math.Min(Environment.ProcessorCount, 62)) - 1);
        _applied = false;
    }

    public static string Describe() => _applied ? $"logical CPU {Core}, high priority" : "no";

    public static string ProcessorName()
    {
        if (OperatingSystem.IsWindows())
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string name) return name.Trim();
        }

        return "unknown processor";
    }
}
