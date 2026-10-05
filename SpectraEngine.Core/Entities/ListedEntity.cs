using System;

namespace SpectraEngine.Core.Entities;

// One entity as ent_list and ent_show see it: what was placed, and the
// running instance when the level plays.
internal readonly record struct ListedEntity(string Name, EntityData Data, Guid NodeId, Entity? Live)
{
    public string ClassName => Data.ClassName;

    // A running entity knows. A placed one can only ask the scene's schemas,
    // and with none of those nothing is called unknown.
    public bool IsUnknownClass(EntitySchemaCatalog? schemas) =>
        Live is not null
            ? Live is PlaceholderEntity
            : schemas is not null && !schemas.TryGetSchema(ClassName, out _);
}
