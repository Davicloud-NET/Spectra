using SpectraEngine.Editing.Hosting;

namespace SpectraEngine.Editor.Shell;

/// <summary>What is being dragged over the viewport and what a drop would cover.</summary>
/// <param name="Scope">What a material would paint. Unused for other kinds.</param>
// A record: DragOver fires per pointer move, and value equality is what
// keeps that to one notification per real change.
public sealed record AssetDragState(ContentDragPayload Payload, MaterialDropScope Scope);
