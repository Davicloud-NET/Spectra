using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace SpectraEngine.Editing.Commands;

/// <summary>Builds the entity a sound file becomes when it is put into a level.</summary>
// The class comes from the scene's schemas, not from a name written here: the
// editor knows entity classes only by what their schemas say.
public static class SoundEntityBuilder
{
    /// <summary>
    /// Finds the class a sound file is placed as: the first one placed at a
    /// point that has a sound file among its settings. With several, that is
    /// the first by name, and <see cref="CountClasses"/> says there were more.
    /// </summary>
    /// <param name="setting">The name of the setting that takes the file.</param>
    public static bool TryFindClass(
        EntitySchemaCatalog? schemas, [NotNullWhen(true)] out EntitySchema? schema, out string setting)
    {
        schema = null;
        setting = string.Empty;

        if (schemas is null)
            return false;

        foreach (EntitySchema candidate in schemas.Schemas)
        {
            if (!TakesSoundFile(candidate, out setting))
                continue;

            schema = candidate;
            return true;
        }

        return false;
    }

    /// <summary>How many classes a sound file could be placed as.</summary>
    public static int CountClasses(EntitySchemaCatalog? schemas)
    {
        if (schemas is null)
            return 0;

        int count = 0;
        foreach (EntitySchema candidate in schemas.Schemas)
        {
            if (TakesSoundFile(candidate, out _))
                count++;
        }

        return count;
    }

    private static bool TakesSoundFile(EntitySchema schema, out string setting)
    {
        setting = string.Empty;

        if (schema.Placement != EntityPlacement.Point)
            return false;

        IReadOnlyList<KeyvalueDescriptor> declared = schema.Keyvalues;
        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i].Type != KeyvalueType.AssetSound)
                continue;

            setting = declared[i].Name;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a detached node carrying a sound entity that plays
    /// <paramref name="contentPath"/>, named after the file. False when it
    /// cannot, and <paramref name="refusal"/> then says why.
    /// </summary>
    public static bool TryBuild(
        EntitySchemaCatalog? schemas,
        string contentPath,
        [NotNullWhen(true)] out SceneNode? node,
        out string refusal)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        node = null;

        if (!TryFindClass(schemas, out EntitySchema? schema, out string setting))
        {
            refusal = "this project has no entity class that plays a sound file";
            return false;
        }

        string path;
        try
        {
            path = ContentRoot.NormalizeRelativePath(contentPath);
        }
        catch (ArgumentException)
        {
            refusal = "that is not a content-relative path";
            return false;
        }

        var entity = new EntityData(schema.ClassName);
        entity.SetValue(setting, path);

        string name = Path.GetFileNameWithoutExtension(path);
        node = new SceneNode(name.Length > 0 ? name : schema.ClassName) { Entity = entity };
        refusal = string.Empty;
        return true;
    }
}
