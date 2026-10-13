namespace SpectraEngine.Scripting.Tests;

// A project's folder on disk, with its Assets folder, in the system's
// temporary folder. Gone when the test ends.
internal sealed class ProjectFolder : IDisposable
{
    public ProjectFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "spectra-script-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "Assets"));
    }

    public string Root { get; }

    public string PathOf(string file) => Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar));

    public bool Has(string file) => File.Exists(PathOf(file));

    public string Read(string file) => File.ReadAllText(PathOf(file));

    public void Write(string file, string text)
    {
        string path = PathOf(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path).ShouldNotBeNull());
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
            return;

        // A test may leave a file read only, and such a file is not deleted.
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(Root, recursive: true);
    }
}
