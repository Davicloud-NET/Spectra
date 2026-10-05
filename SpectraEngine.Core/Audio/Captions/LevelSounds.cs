using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Audio.Captions;

// The sound files a level's entities are set to play.
internal static class LevelSounds
{
    // Appends each sound once, as a normalized content path, in ordinal order.
    // A sound is any setting a class declares as one, whatever the class.
    public static void Collect(Scene.Scene scene, EntitySchemaCatalog schemas, List<string> sounds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SceneNode node in scene.Root.Traverse())
        {
            if (node.Entity is not { } entity || !schemas.TryGetSchema(entity.ClassName, out EntitySchema? schema))
                continue;

            foreach (KeyvalueDescriptor setting in schema.Keyvalues)
            {
                if (setting.Type != KeyvalueType.AssetSound)
                    continue;

                string path = entity.TryGetValue(setting.Name, out string set) ? set : setting.Default;
                if (TryNormalize(path, out string sound) && seen.Add(sound))
                    sounds.Add(sound);
            }
        }

        sounds.Sort(StringComparer.Ordinal);
    }

    private static bool TryNormalize(string path, out string sound)
    {
        sound = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            sound = ContentRoot.NormalizeRelativePath(path);
            return true;
        }
        catch (ArgumentException)
        {
            // Not a content path. The level's log says so when it starts.
            return false;
        }
    }
}
