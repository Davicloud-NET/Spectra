using SpectraEngine.Core.Audio;
using System.Reflection;

namespace SpectraEngine.Bsp.Tests;

// AL_LOOPING repeats a whole buffer and cannot express a loop region, so the
// engine never uses it. No audio device in CI, so the rule is checked in the
// interface and in the source text.
public sealed class AudioLoopingConventionTests
{
    [Fact]
    public void The_backend_seam_cannot_express_a_whole_buffer_loop_at_all()
    {
        MethodInfo[] members = typeof(IAudioBackend).GetMethods();

        foreach (MethodInfo member in members)
        {
            member.Name.ShouldNotContain("Loop", Case.Insensitive,
                "the AL seam must have no way to ask for AL_LOOPING; loops are buffer-queue arithmetic");

            foreach (ParameterInfo parameter in member.GetParameters())
            {
                (parameter.Name ?? string.Empty).ShouldNotContain("loop", Case.Insensitive,
                    $"{member.Name} takes a loop parameter, which the buffer-queue design has no use for");
            }
        }
    }

    [Fact]
    public void The_only_source_naming_the_AL_looping_enum_clears_it()
    {
        string audio = Path.Combine(SourceRoot(), "SpectraEngine.Core", "Audio");
        Directory.Exists(audio).ShouldBeTrue($"expected the audio sources under {audio}");

        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(audio, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (IsComment(line)) continue;
                if (!line.Contains("SourceBoolean.Looping", StringComparison.Ordinal)) continue;

                // Clearing it on a pooled source is the one allowed use.
                if (line.Contains("false", StringComparison.Ordinal)) continue;

                offenders.Add($"{Path.GetFileName(file)}({i + 1}): {line.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "AL_LOOPING repeats a whole buffer and cannot express a loop region inside one; " +
            "loops go through AudioLoopCursor and a buffer queue instead");
    }

    private static bool IsComment(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal);
    }

    // Repo root: the nearest ancestor holding a solution file.
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
