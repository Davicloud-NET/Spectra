using Silk.NET.Input;
using Silk.NET.Input.Glfw;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core;

/// <summary>
/// Registers the Silk.NET windowing and input backends explicitly, so window
/// and input creation does not depend on reflection-based platform discovery.
/// </summary>
// Silk.NET finds its GLFW backends by Assembly.Load and Activator. A NativeAOT
// or trimmed publish drops those assemblies and the process dies at window
// creation with "Couldn't find a suitable window platform".
public static class SilkPlatform
{
    /// <summary>
    /// Registers the GLFW windowing and input platforms and turns off Silk.NET's
    /// backend discovery. Call on the window thread before the first
    /// <see cref="Window.Create"/> or <c>CreateInput</c>. Safe to call more than once.
    /// </summary>
    public static void EnsureRegistered()
    {
        UsePortableRuntimeId();

        // Register first: disabling discovery only marks the platforms as
        // loaded, it does not supply them.
        GlfwWindowing.RegisterPlatform();
        GlfwInput.RegisterPlatform();

        Window.ShouldLoadFirstPartyPlatforms(false);
        InputWindowExtensions.ShouldLoadFirstPartyPlatforms(false);
    }

    /// <summary>
    /// Lets Silk.NET find its native libraries on Linux. Call before the first
    /// <c>GetApi()</c>. Safe to call more than once.
    /// </summary>
    // Silk.NET looks under runtimes/<rid>/native with the distro rid
    // ("ubuntu.24.04-x64"), which Microsoft's runtime builds don't map to
    // linux-x64. DOTNET_RUNTIME_ID is the override its lookup reads.
    public static void UsePortableRuntimeId()
    {
        const string Variable = "DOTNET_RUNTIME_ID";
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable(Variable) is null)
            Environment.SetEnvironmentVariable(Variable, RuntimeInformation.RuntimeIdentifier);
    }
}
