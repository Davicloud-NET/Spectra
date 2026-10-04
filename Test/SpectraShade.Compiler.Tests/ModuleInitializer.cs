using System.IO;
using System.Runtime.CompilerServices;
using EmptyFiles;
using VerifyTests;

namespace SpectraShade.Compiler.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        Verifier.DerivePathInfo((sourceFile, projectDirectory, type, method) =>
            new PathInfo(
                directory: Path.Combine(projectDirectory, "Snapshots"),
                typeName: type.Name,
                methodName: method.Name));

        // So Verify diffs shader output as text.
        FileExtensions.AddTextExtension("hlsl");
        FileExtensions.AddTextExtension("glsl");
        FileExtensions.AddTextExtension("vert");
        FileExtensions.AddTextExtension("frag");
        FileExtensions.AddTextExtension("geom");
        FileExtensions.AddTextExtension("comp");
    }
}
