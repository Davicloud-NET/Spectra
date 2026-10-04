using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Scans the graphics sources: none may write <c>new ComPtr&lt;T&gt;(p)</c>,
/// which AddRefs and leaks the resource.
/// </summary>
public sealed class ComPtrOwnershipConventionTests
{
    // Must not match `new ComPtr<T>[n]`, an array of empty handles.
    private static readonly Regex Wrap = new(@"new\s+ComPtr<[^>]+>\s*\(", RegexOptions.Compiled);

    [Fact]
    public void No_D3D_source_wraps_a_raw_pointer_instead_of_owning_it()
    {
        var offenders = new List<string>();

        foreach (string file in GraphicsSources())
        {
            if (Path.GetFileName(file) == "ComOwnership.cs") continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (IsComment(line)) continue;
                if (Wrap.IsMatch(line))
                    offenders.Add($"{Path.GetFileName(file)}({i + 1}): {line.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "every freshly created COM pointer must be handed to ComOwnership.Own; " +
            "`new ComPtr<T>(p)` AddRefs and the resource is then never destroyed");
    }

    private static bool IsComment(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal);
    }

    private static IEnumerable<string> GraphicsSources()
    {
        string root = SourceRoot();
        string graphics = Path.Combine(root, "SpectraEngine.Core", "Graphics");
        Directory.Exists(graphics).ShouldBeTrue($"expected the graphics sources under {graphics}");
        return Directory.EnumerateFiles(graphics, "*.cs", SearchOption.AllDirectories);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"No solution file above {AppContext.BaseDirectory}; the source-convention test needs the repo.");
    }
}
