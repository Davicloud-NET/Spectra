using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Scripting;

/// <summary>A node a script cloned, beside the copy that was made of it.</summary>
/// <param name="Source">The node that was copied.</param>
/// <param name="Copy">Its copy, which a script made while the level played.</param>
public readonly record struct NodeCopy(SceneNode Source, SceneNode Copy);
