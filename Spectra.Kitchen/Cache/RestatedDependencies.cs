using Spectra.Kitchen.Rules;
using System;
using System.Collections.Generic;
using System.IO;

namespace Spectra.Kitchen.Cache;

// Asks a recorded run's dependencies again against the files as they are now.
// The key over the answer equals the recorded key while nothing the rule
// touched has changed, appeared or gone.
internal static class RestatedDependencies
{
    // False when there is nothing readable at the path.
    public delegate bool HashOf(string contentPath, string fullPath, out UInt128 hash);

    // Keeps the recorded order, or the restated key cannot match the recorded one.
    public static RuleDependency[] Of(
        string contentRoot, IReadOnlyList<RuleDependency> recorded, HashOf hashOf)
    {
        var restated = new RuleDependency[recorded.Count];
        for (int i = 0; i < recorded.Count; i++)
        {
            RuleDependency dependency = recorded[i];
            string full = Path.Combine(
                contentRoot, dependency.Path.Replace('/', Path.DirectorySeparatorChar));

            restated[i] = dependency.Kind switch
            {
                // Contents are in the key, so hash again.
                RuleDependencyKind.Read => hashOf(dependency.Path, full, out UInt128 hash)
                    ? new RuleDependency(dependency.Path, RuleDependencyKind.Read, hash)
                    : new RuleDependency(dependency.Path, RuleDependencyKind.ProbeMissing, UInt128.Zero),

                // A probe only asked whether the file exists. No hash.
                _ => File.Exists(full)
                    ? new RuleDependency(dependency.Path, RuleDependencyKind.ProbeFound, UInt128.Zero)
                    : new RuleDependency(dependency.Path, RuleDependencyKind.ProbeMissing, UInt128.Zero),
            };
        }

        return restated;
    }
}
