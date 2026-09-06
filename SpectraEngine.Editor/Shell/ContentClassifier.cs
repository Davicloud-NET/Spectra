using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// What a file in a project is, from its extension.
/// </summary>
/// <remarks>
/// <b>One table, because three readers ask.</b> The content browser lists files,
/// the asset picker offers them and a drag payload carries the answer to the
/// drop; a second copy of this switch would disagree the first time somebody
/// added an extension to one of them, and the way that presents is a file the
/// browser draws as a material and the picker cannot find.
/// </remarks>
public static class ContentClassifier
{
    /// <summary>Classifies a file by its extension. A folder is never asked.</summary>
    public static ContentKind Classify(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" => ContentKind.Texture,
        ".spectramat" => ContentKind.Material,
        ".obj" or ".gltf" or ".glb" or ".fbx" or ".mtl" => ContentKind.Model,
        ".spectrashade" => ContentKind.Shader,
        _ => ContentKind.Other,
    };

    /// <summary>The word a panel shows for a kind.</summary>
    public static string Label(ContentKind kind) => kind switch
    {
        ContentKind.Folder => "Folder",
        ContentKind.Texture => "Texture",
        ContentKind.Material => "Material",
        ContentKind.Model => "Model",
        ContentKind.Shader => "Shader",
        _ => "File",
    };
}
