using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

// Throws when the level stops, as an entity with a fault in OnRemove would.
internal sealed class FailingRemovalEntity : Entity
{
    public const string Class = "fails_on_remove";

    public static EntityCatalog AddTo(EntityCatalog catalog)
    {
        catalog.Add(new EntitySchema(Class), () => new FailingRemovalEntity());
        return catalog;
    }

    protected internal override void OnRemove() =>
        throw new InvalidOperationException("This entity cannot be removed.");
}
