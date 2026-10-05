using System;
using System.Diagnostics;
using System.IO;
using Silk.NET.OpenAL;

namespace OpenAlFilterSpike.Checks;

// Question 6: how a library with no filters shows itself.
internal static unsafe class MissingChecks
{
    internal static void Run(Report report, OpenAl lib)
    {
        using (Rig rig = Rig.Open(lib))
        {
            bool extension = lib.Alc.IsExtensionPresent(rig.Device, "ALC_EXT_NOT_A_THING");
            nint function = (nint)rig.Al.GetProcAddress("alGenFiltersNotAThing");
            int constant = rig.Al.GetEnumValue("AL_NOT_A_THING");
            AudioError error = rig.Al.GetError();
            report.Check("6a", "names the library does not know", !extension && function == 0 && constant == 0,
                $"extension {extension}, function {(function == 0 ? "null" : "not null")}, constant {constant}, alGetError {error}");
        }

        // The only library here that could lack EFX is one the system installed.
        try
        {
            using OpenAl system = OpenAl.LoadSystem();
            if (system.Loopback is null)
            {
                report.Info("6b a system OpenAL", "loaded, has no loopback device, not probed further");
            }
            else
            {
                Device* device = system.Loopback.OpenDevice(null);
                bool efx = system.Alc.IsExtensionPresent(device, Efx.ExtensionName);
                string name = system.Alc.GetContextProperty(device, GetContextString.DeviceSpecifier);
                system.Alc.CloseDevice(device);
                report.Info("6b a system OpenAL", $"loaded, device \"{name}\", EFX {(efx ? "present" : "absent")}");
            }

            ListModules(report);
        }
        catch (Exception e) // boundary: no system OpenAL is the expected case
        {
            report.Info("6b a system OpenAL", $"none could be loaded ({e.GetType().Name})");
            ListModules(report);
        }
    }

    // Says which files the loads really opened.
    private static void ListModules(Report report)
    {
        using Process self = Process.GetCurrentProcess();
        foreach (ProcessModule module in self.Modules)
        {
            string file = Path.GetFileName(module.FileName);
            if (file.StartsWith("soft_oal", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("openal32", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("libopenal", StringComparison.OrdinalIgnoreCase))
            {
                report.Info("6b OpenAL module in the process", module.FileName);
            }
        }
    }
}
