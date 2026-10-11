using SpectraEngine.Core.Entities;
using SpectraEngine.Entities;
using System.Runtime.CompilerServices;

namespace SpectraEngine.Editor.Tests;

// The engine's own entity classes, as the editor learns them: parsed from
// .sentdef bytes.
internal static class BuiltinClasses
{
    private static EntitySchemaCatalog? _catalog;

    public static EntitySchemaCatalog Catalog =>
        _catalog ??= EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(BuiltinEntities.Schemas));

    // The classes register as their assembly loads, and that throws once
    // anything has read EntityCatalog.Shared. Some test here reads it, so the
    // assembly is loaded before the first test runs.
    [ModuleInitializer]
    internal static void Register() => BuiltinEntities.EnsureRegistered();
}
