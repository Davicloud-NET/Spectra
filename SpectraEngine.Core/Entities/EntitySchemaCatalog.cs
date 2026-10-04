using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The entity classes parsed from a <c>.sentdef</c> image: schemas only, with no
/// factory and no way to build an instance. Immutable once loaded.
/// </summary>
// LoadFromSentDef must stay the only way in. A constructor taking a schema
// list would let an in-process editor skip the file and drift from one that
// reads it.
public sealed class EntitySchemaCatalog
{
    private readonly EntitySchema[] _schemas;
    private readonly Dictionary<string, EntitySchema> _byClassName;

    private EntitySchemaCatalog(EntitySchema[] schemas)
    {
        _schemas = schemas;

        _byClassName = new Dictionary<string, EntitySchema>(schemas.Length, StringComparer.Ordinal);
        foreach (EntitySchema schema in schemas)
            _byClassName.Add(schema.ClassName, schema);
    }

    /// <summary>Parses a complete <c>.sentdef</c> image. Nothing is retained past the call.</summary>
    /// <exception cref="SentDefFormatException">The image is not one this build can read.</exception>
    public static EntitySchemaCatalog LoadFromSentDef(ReadOnlySpan<byte> image) =>
        new(SentDef.Read(image));

    /// <summary>Every class the image declared, sorted by class name (ordinal).</summary>
    public IReadOnlyList<EntitySchema> Schemas => _schemas;

    /// <summary>How many classes this catalogue describes.</summary>
    public int Count => _schemas.Length;

    /// <summary>
    /// The schema declared for <paramref name="className"/>. False for an
    /// unknown class, which is normal for a map from another game.
    /// </summary>
    public bool TryGetSchema(string? className, [NotNullWhen(true)] out EntitySchema? schema)
    {
        if (className is not null)
            return _byClassName.TryGetValue(className, out schema);

        schema = null;
        return false;
    }
}
