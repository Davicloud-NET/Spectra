using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Assets;
using System;
using System.IO;

namespace SpectraEngine.Entities.Tests;

// The sounds in the content root, cooked into a folder of their own the way
// the demo's build cooks them. Made once for a test class.
public sealed class CookedDemoSounds : IDisposable
{
    public CookedDemoSounds() => Cook = LooseSoundFolder.Cook(ContentRoot.Path, Folder);

    public string Folder { get; } =
        Path.Combine(Path.GetTempPath(), "SpectraDemoSounds", Guid.NewGuid().ToString("N"));

    // What the cook said about them.
    public LooseSoundFolderResult Cook { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // Never made, or locked. Neither is a test failure.
        }
    }
}
