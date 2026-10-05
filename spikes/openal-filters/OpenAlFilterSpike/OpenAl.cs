using System;
using System.Runtime.InteropServices;
using Silk.NET.OpenAL;

namespace OpenAlFilterSpike;

/// <summary>The loaded library: both API objects and the loopback entry points.</summary>
internal sealed class OpenAl : IDisposable
{
    private OpenAl(AL al, ALContext alc)
    {
        Al = al;
        Alc = alc;
        Loopback = Loopback.TryLoad(alc);
    }

    internal AL Al { get; }

    internal ALContext Alc { get; }

    internal Loopback? Loopback { get; }

    /// <summary>Loads the packaged OpenAL Soft the way the engine does.</summary>
    internal static OpenAl LoadSoft()
    {
        // Same as SilkPlatform.UsePortableRuntimeId in the engine: Silk.NET
        // looks under runtimes/<rid>/native with the distro rid otherwise.
        const string Variable = "DOTNET_RUNTIME_ID";
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable(Variable) is null)
            Environment.SetEnvironmentVariable(Variable, RuntimeInformation.RuntimeIdentifier);

        return new OpenAl(AL.GetApi(soft: true), ALContext.GetApi(soft: true));
    }

    /// <summary>Loads whatever OpenAL the system has installed. Throws when there is none.</summary>
    internal static OpenAl LoadSystem() => new(AL.GetApi(soft: false), ALContext.GetApi(soft: false));

    public void Dispose()
    {
        Al.Dispose();
        Alc.Dispose();
    }
}
