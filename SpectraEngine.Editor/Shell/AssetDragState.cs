using SpectraEngine.Editing.Hosting;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// What is being dragged over the viewport, and how much of what is under it
/// letting go would cover.
/// </summary>
/// <remarks>
/// <b>A record, and the record equality IS the notification guard.</b>
/// <c>DragOver</c> arrives per pointer move, and every one of them carries the
/// payload the gesture started with: the state changes exactly when the file
/// changes or the modifier does, so comparing the whole value raises one
/// notification per Ctrl press instead of several hundred per crossing. Held by
/// reference on the viewport, compared by value here, which is why this is a
/// record rather than a tuple.
/// </remarks>
/// <param name="Payload">The file under the pointer.</param>
/// <param name="Scope">
/// What a material would paint: the face under the pointer, or the whole block
/// while Ctrl is held. Meaningless for anything that is not a material, and
/// carried anyway so the prompt does not have to ask twice.
/// </param>
public sealed record AssetDragState(ContentDragPayload Payload, MaterialDropScope Scope);
