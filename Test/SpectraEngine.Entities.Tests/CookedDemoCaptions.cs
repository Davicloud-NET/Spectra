using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Projects;
using System;
using System.IO;

namespace SpectraEngine.Entities.Tests;

// The content root's sounds, caption files and subtitles as a project of
// their own, cooked into a pack the way scook cooks one. Textures and models
// are left out: no caption or subtitle check reads them, and they are slow
// to cook. Made once for a test class.
public sealed class CookedDemoCaptions : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SpectraDemoCaptions", Guid.NewGuid().ToString("N"));

    public CookedDemoCaptions()
    {
        ProjectLayout project = ProjectLayout.Create(_root, "Demo");

        foreach (ContentFile file in ContentWalker.Walk(ContentRoot.Path))
        {
            if (!IsReadByACaptionCheck(file.ContentPath))
                continue;

            string target = ContentRoot.ResolveAbsolute(project.AssetsPath, file.ContentPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? project.AssetsPath);
            File.Copy(file.FullPath, target);
        }

        Cook = new CookSession(project, new CookSettings { UseCache = false }).Run();
    }

    // What the cook did and said.
    public CookResult Cook { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Never made, or locked. Neither is a test failure.
        }
    }

    private static bool IsReadByACaptionCheck(string contentPath) =>
        CaptionFile.IsInCaptionFolder(contentPath)
        || SubtitlePath.IsSubtitle(contentPath)
        || AudioRule.Handles(contentPath)
        || AudioContentPath.IsCooked(contentPath);
}
