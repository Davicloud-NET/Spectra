using System;

namespace SpectraEngine.Editing.Hosting;

/// <summary>
/// An entity made from geometry that still has world geometry in it. It
/// refuses to move when the level plays.
/// </summary>
/// <param name="EntityId">The entity's node.</param>
/// <param name="EntityName">That node's name, which is what a wire targets.</param>
/// <param name="ClassName">The entity's class.</param>
/// <param name="BrushId">The first world brush at or below the entity.</param>
/// <param name="BrushName">That brush node's name.</param>
/// <param name="IsCut">Whether that brush cuts solid instead of adding it.</param>
public readonly record struct EntityProblem(
    Guid EntityId,
    string EntityName,
    string ClassName,
    Guid BrushId,
    string BrushName,
    bool IsCut)
{
    /// <summary>One line for a problem list, ending in the way out.</summary>
    public string Describe()
    {
        string what = EntityId == BrushId
            ? $"'{EntityName}' is a {ClassName}, but it is still a {Kind}."
            : $"'{EntityName}' is a {ClassName}, but '{BrushName}' inside it is still a {Kind}.";

        string fix = IsCut
            ? "Move the cut out of it."
            : "Select the block and convert it to a part (Ctrl+T).";

        return $"{what} It will not work when the level plays. {fix}";
    }

    private string Kind => IsCut ? "cut" : "block";
}
