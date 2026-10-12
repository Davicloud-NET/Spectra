using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// A container and where it is declared, for an error only the whole API
// shows. Kept apart from the catalogue's entries: a place moves with every
// edit above it.
internal sealed record ScriptContainerPlace(
    ScriptCatalogEntry Entry,
    LocationInfo? Location) : IEquatable<ScriptContainerPlace>;
