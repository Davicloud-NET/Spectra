using Microsoft.Extensions.Logging;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.D3D11;
using SpectraEngine.Core.Graphics.D3D12;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Physics.Box3D;
using SpectraShade.Compiler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace SpectraEngine.Editor;

/// <summary>
/// One running engine inside the shell: the subsystems, the editor, and the
/// lifetime that ties them to a viewport surface. D3D11 and D3D12 only.
/// </summary>
public sealed class EditorSession : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<EditorSession> _logger;
    private readonly Renderer _renderer;
    private readonly Engine _engine;

    /// <summary>Builds a session on the given backend, without starting it.</summary>
    /// <param name="loggerFactory">Owned by the caller.</param>
    /// <param name="backend">D3D11 or D3D12.</param>
    /// <param name="contentRoot">
    /// The asset content root, or null for the engine's default. Fixed for the
    /// session's life; opening another project is a new session.
    /// </param>
    public EditorSession(ILoggerFactory loggerFactory, GraphicsBackend backend, string? contentRoot = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<EditorSession>();

        var shaderCompiler = new SpectraShadeCompiler();
        _renderer = backend switch
        {
            GraphicsBackend.D3D11 => new D3D11Renderer(loggerFactory.CreateLogger<D3D11Renderer>(), shaderCompiler),
            GraphicsBackend.D3D12 => new D3D12Renderer(loggerFactory.CreateLogger<D3D12Renderer>(), shaderCompiler),
            _ => throw new NotSupportedException(
                $"The editor viewport cannot host {backend} yet: an embedded OpenGL surface needs its own " +
                "WGL context and proc-address loader, which is not built. Use d3d11 or d3d12."),
        };

        // Uncapped, the viewport presents thousands of frames a second and the
        // gen0 pauses stall the UI thread too.
        _renderer.VSync = true;

        // Schemas go through .sentdef bytes, never straight from EntityCatalog:
        // the editor must read the same thing whether a class came from C# or
        // from a file. One instance, shared with the scene manager, so the UI
        // thread and the render thread see the same catalogue.
        EntitySchemas = EntitySchemaCatalog.LoadFromSentDef(
            SentDef.Write(EntityCatalog.Shared.Schemas));

        var sceneManager = new SceneManager(loggerFactory.CreateLogger<SceneManager>())
        {
            // Boot into the baseplate; the real map opens through OpenMap,
            // where a bad bundle reports.
            Startup = StartupSceneKind.Baseplate,
            EntitySchemas = EntitySchemas,
        };
        var assetManager = contentRoot is null
            ? new AssetManager(loggerFactory.CreateLogger<AssetManager>())
            : new AssetManager(loggerFactory.CreateLogger<AssetManager>(), contentRoot);
        var audioManager = new AudioManager(loggerFactory.CreateLogger<AudioManager>());
        var inputManager = new InputManager(loggerFactory.CreateLogger<InputManager>());

        // Runs on the render thread once the scene exists.
        sceneManager.EditorFactory = scene =>
            new SceneEditorHost(loggerFactory, scene, _renderer, inputManager);

        sceneManager.PhysicsFactory = _ =>
            new Box3DScenePhysics(loggerFactory.CreateLogger<Box3DScenePhysics>());

        SceneManager = sceneManager;
        _engine = new Engine(
            loggerFactory.CreateLogger<Engine>(),
            _renderer,
            sceneManager,
            assetManager,
            audioManager,
            inputManager);
    }

    /// <summary>The scene manager, for the panels that report on it.</summary>
    public SceneManager SceneManager { get; }

    /// <summary>
    /// The entity classes this session can place and describe. Immutable, and
    /// the same instance every scene it loads uses.
    /// </summary>
    public EntitySchemaCatalog EntitySchemas { get; }

    /// <summary>The surface a UI thread drives this engine through.</summary>
    public EngineHost Host => _engine.Host;

    /// <summary>
    /// Sends a typed line to the engine's console, as typed. What it prints
    /// comes back in <see cref="FrameSnapshot.ConsoleLines"/>. False when
    /// too many lines are already waiting.
    /// </summary>
    public bool SubmitConsoleLine(string line) => Host.SubmitConsoleLine(line);

    /// <summary>Whether the render thread is running.</summary>
    public bool IsRunning => _engine.IsRunning;

    /// <summary>
    /// Why the render thread ended on an exception, or null while it has not.
    /// Safe from any thread.
    /// </summary>
    public EngineFault? Fault => _engine.Fault;

    /// <summary>
    /// Whether the render thread died and has finished dying, so nothing is
    /// drawing and <see cref="CaptureLevelAfterFault"/> may be called. Safe
    /// from any thread.
    /// </summary>
    public bool HasDied => _engine.RenderThreadExited && _engine.Fault is not null;

    /// <summary>
    /// Takes the level out of a session that has died, so a new session can
    /// carry on from it. A level that was playing is stopped first. Null when
    /// the session died before it had a scene. Call before <see cref="Stop"/>,
    /// which drops the scene.
    /// </summary>
    /// <param name="report">Records what a document cannot hold, such as a mesh built in code.</param>
    /// <exception cref="InvalidOperationException">The session has not died.</exception>
    // The only place a UI thread reads the scene. The render thread owns it
    // while it lives, so this is legal only once that thread is gone.
    public SessionRemains? CaptureLevelAfterFault(MapSaveReport? report = null)
    {
        if (!HasDied)
        {
            throw new InvalidOperationException(
                "The scene can be read from here only after the render thread has ended on a fault.");
        }

        // As authored: running entities have moved nodes, and a save is
        // refused during play for that reason.
        if (SceneManager.TakeAuthoredMap(report) is not { } level)
            return null;

        ISceneEditor? editor = SceneManager.Editor;
        return new SessionRemains(level, editor?.UndoDepth ?? 0, editor?.RedoDepth ?? 0);
    }

    /// <summary>Starts the engine against a viewport surface.</summary>
    public void Start(IRenderSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _engine.Start(surface);
        _logger.LogInformation("Editor session started on {Backend}", _renderer.GetType().Name);
    }

    // Everything below posts to the render thread and uses the same verbs a
    // key chord does. Editor is read inside the queued command because only
    // the render thread may read SceneManager.Editor.

    /// <summary>Runs one host verb: history, a structural edit, a mode toggle.</summary>
    public void Post(EditorHostCommand command) =>
        Host.EnqueueCommand(_ => Editor?.Apply(command));

    /// <summary>Runs one manipulator verb: pick a tool, flip a mode, drive snap.</summary>
    public void Post(GizmoCommand command) =>
        Host.EnqueueCommand(_ => Editor?.Apply(command));

    /// <summary>Runs one camera verb, such as framing the selection.</summary>
    public void Post(EditorCameraCommand command) =>
        Host.EnqueueCommand(_ => Editor?.Apply(command));

    /// <summary>Sets one tool's snap increment.</summary>
    public void SetSnapIncrement(GizmoMode tool, float increment) =>
        Host.EnqueueCommand(_ => Editor?.SetSnapIncrement(tool, increment));

    /// <summary>
    /// Creates one thing at a viewport point, or at the view centre when
    /// none is given.
    /// </summary>
    public void Insert(InsertKind kind, Vector2? viewportPoint = null) =>
        Host.EnqueueCommand(_ => Editor?.Insert(kind, viewportPoint));

    /// <summary>Creates one entity of a named class where the user is looking.</summary>
    public void InsertEntity(string className, Vector2? viewportPoint = null) =>
        Host.EnqueueCommand(_ => Editor?.InsertEntity(className, viewportPoint));

    /// <summary>
    /// Makes the selected block, part or group an entity of a named class, as
    /// one history entry.
    /// </summary>
    /// <param name="done">Called on the render thread with what happened.</param>
    public void MakeEntity(string className, Action<EntityEditReport> done)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentNullException.ThrowIfNull(done);

        Host.EnqueueCommand(_ => done(
            Editor is { } editor
                ? editor.MakeEntity(className)
                : EntityEditReport.RefusedBecause("Make entity did nothing: the session has no editor yet.")));
    }

    /// <summary>Takes the entity off the selected nodes, as one history entry.</summary>
    /// <param name="done">Called on the render thread with what happened.</param>
    public void RemoveEntity(Action<EntityEditReport> done)
    {
        ArgumentNullException.ThrowIfNull(done);

        Host.EnqueueCommand(_ => done(
            Editor is { } editor
                ? editor.RemoveEntity()
                : EntityEditReport.RefusedBecause("Remove entity did nothing: the session has no editor yet.")));
    }

    /// <summary>
    /// Lists the entities that have world geometry in them and so will not
    /// work when the level plays.
    /// </summary>
    /// <param name="done">Called on the render thread with the list, empty when all is well.</param>
    public void FindEntityProblems(Action<IReadOnlyList<EntityProblem>> done)
    {
        ArgumentNullException.ThrowIfNull(done);

        Host.EnqueueCommand(_ => done(Editor?.FindEntityProblems() ?? []));
    }

    /// <summary>
    /// Places a model file in the scene. A model that cannot be resolved still
    /// places a node; the report says which happened.
    /// </summary>
    /// <param name="contentPath">The model, relative to the content root.</param>
    /// <param name="viewportPoint">Where to place it, in viewport pixels.</param>
    /// <param name="done">Called on the render thread with what happened.</param>
    public void InsertModel(string contentPath, Vector2? viewportPoint, Action<ModelInsertReport> done)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);
        ArgumentNullException.ThrowIfNull(done);

        Host.EnqueueCommand(_ => done(
            Editor is { } editor
                ? editor.InsertModel(contentPath, viewportPoint)
                // Only while the session is still coming up.
                : ModelInsertReport.RefusedBecause(contentPath, "the session has no editor yet")));
    }

    /// <summary>
    /// Paints a material onto the face under a viewport point, or onto the whole
    /// brush there. An empty path means no material.
    /// </summary>
    public void AssignMaterial(
        string contentPath, Vector2? viewportPoint, MaterialDropScope scope,
        Action<MaterialAssignReport> done)
    {
        ArgumentNullException.ThrowIfNull(contentPath);
        ArgumentNullException.ThrowIfNull(done);

        Host.EnqueueCommand(_ => done(
            Editor is { } editor
                ? editor.AssignMaterial(contentPath, viewportPoint, scope)
                : MaterialAssignReport.RefusedBecause(contentPath, "the session has no editor yet")));
    }

    /// <summary>Paints every selected brush, whole, in one history entry.</summary>
    public void AssignMaterialToSelection(string contentPath, Action<MaterialAssignReport> done)
    {
        ArgumentNullException.ThrowIfNull(contentPath);
        ArgumentNullException.ThrowIfNull(done);

        Host.EnqueueCommand(_ => done(
            Editor is { } editor
                ? editor.AssignMaterialToSelection(contentPath)
                : MaterialAssignReport.RefusedBecause(contentPath, "the session has no editor yet")));
    }

    /// <summary>
    /// Says whether a material drag is over the viewport, so the outline can
    /// show what letting go would paint. Post on a change, not per pointer move.
    /// </summary>
    public void SetMaterialDrag(MaterialDropScope? scope) =>
        Host.EnqueueCommand(_ => Editor?.SetMaterialDrag(scope));

    /// <summary>Selects the node with this id. An id the scene no longer has is ignored.</summary>
    public void Select(Guid nodeId, SelectionUpdate mode = SelectionUpdate.Replace) =>
        Host.EnqueueCommand(_ => Editor?.SelectById(nodeId, mode));

    /// <summary>Selects a set of ids in one batch. Unresolvable ids are skipped.</summary>
    public void SelectMany(IReadOnlyList<Guid> nodeIds, SelectionUpdate mode = SelectionUpdate.Replace) =>
        Host.EnqueueCommand(_ => Editor?.SelectByIds(nodeIds, mode));

    /// <summary>
    /// Selects whatever sits under a viewport point unless it is already
    /// selected. Run before a context menu opens.
    /// </summary>
    public void SelectAtPoint(Vector2 viewportPoint) =>
        Host.EnqueueCommand(_ => Editor?.SelectAtPoint(viewportPoint));

    /// <summary>Renames one node, addressed by id, as one history entry.</summary>
    public void Rename(Guid nodeId, string name) =>
        Host.EnqueueCommand(_ => Editor?.RenameById(nodeId, name));

    /// <summary>
    /// Moves nodes under a new parent at an index (-1 appends), keeping world
    /// transforms.
    /// </summary>
    public void Reparent(IReadOnlyList<Guid> nodeIds, Guid newParentId, int insertIndex) =>
        Host.EnqueueCommand(_ => Editor?.ReparentByIds(nodeIds, newParentId, insertIndex));

    /// <summary>Applies one property-panel edit to the current selection.</summary>
    public void ApplyProperty(PropertyEdit edit) =>
        Host.EnqueueCommand(_ => Editor?.ApplyProperty(edit));

    /// <summary>
    /// Opens one history entry to hold a continuous property gesture, such as
    /// a drag across a numeric field.
    /// </summary>
    // If the editor refuses, each edit becomes its own history entry and the
    // matching End does nothing.
    public void BeginPropertyGesture(string name) =>
        Host.EnqueueCommand(_ => Editor?.BeginPropertyGesture(name));

    /// <summary>Closes a property gesture, keeping its result or rolling it back.</summary>
    public void EndPropertyGesture(bool commit) =>
        Host.EnqueueCommand(_ => Editor?.EndPropertyGesture(commit));

    /// <summary>Replaces the whole connection list on one entity node.</summary>
    // By id, not by selection: a stale selection would overwrite another
    // entity's wiring.
    public void ApplyEntityConnections(Guid nodeId, IReadOnlyList<EntityConnection> connections) =>
        Host.EnqueueCommand(_ => Editor?.ApplyEntityConnections(nodeId, connections));

    // Render thread only. Null before the scene has loaded.
    private SceneEditorHost? Editor => SceneManager.Editor as SceneEditorHost;

    // The document verbs run on the render thread, and so do their callbacks.

    /// <summary>
    /// Writes the live scene into a map bundle. Refused while the level is
    /// playing.
    /// </summary>
    /// <param name="bundlePath">The <c>.smap</c> directory to write.</param>
    /// <param name="done">
    /// Called on the render thread with the save report, or the failure. An
    /// incomplete report is still a successful save.
    /// </param>
    public void SaveMap(string bundlePath, Action<MapSaveReport?, Exception?> done)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentNullException.ThrowIfNull(done);

        Host.EnqueueCommand(scene =>
        {
            // Asked here, not in the shell: its snapshot can be a frame old.
            if (SaveRefusal(SceneManager.EntityWorld) is { } refusal)
            {
                done(null, refusal);
                return;
            }

            try
            {
                var report = new MapSaveReport();
                MapBundle.Save(bundlePath, MapSceneBinder.FromScene(scene, report));
                done(report, null);
            }
            catch (Exception ex) when (ex is MapFormatException or IOException or UnauthorizedAccessException)
            {
                done(null, ex);
            }
        });
    }

    // Why a save must not run now, or null when it may. Running entities have
    // moved nodes, and the save would keep those poses as the authored ones.
    internal static InvalidOperationException? SaveRefusal(EntityWorld? entities) =>
        entities is { IsActive: true }
            ? new InvalidOperationException(
                "it is playing, and a save now would keep what the run has moved. Stop it first.")
            : null;

    /// <summary>
    /// Replaces the live scene's graph with a map bundle's. <c>done</c> runs on
    /// the render thread.
    /// </summary>
    public void OpenMap(string bundlePath, Action<MapLoadReport?, Exception?> done)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentNullException.ThrowIfNull(done);

        Host.EnqueueCommand(scene => ReplaceGraph(scene, () => MapBundle.Load(bundlePath), done));
    }

    /// <summary>
    /// Replaces the live scene's graph with a document that is already in
    /// memory. <c>done</c> runs on the render thread.
    /// </summary>
    public void ApplyMap(MapDocument document, Action<MapLoadReport?, Exception?> done)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(done);

        Host.EnqueueCommand(scene => ReplaceGraph(scene, () => document, done));
    }

    // Render thread.
    private void ReplaceGraph(
        Scene scene, Func<MapDocument> read, Action<MapLoadReport?, Exception?> done)
    {
        try
        {
            MapDocument document = read();

            // Reset before the graph changes: the entity runtime, the
            // selection and the undo history all refer to the old nodes,
            // and a load keeps node ids.
            SceneManager.OnSceneReplaced();
            Editor?.OnSceneReplaced();

            var report = new MapLoadReport();
            MapSceneBinder.ApplyTo(document, scene, report);
            scene.RebuildStaticWorld(_renderer);

            done(report, null);
        }
        catch (Exception ex) when (
            ex is MapFormatException or IOException or UnauthorizedAccessException)
        {
            done(null, ex);
        }
    }

    /// <summary>
    /// Replaces the scene with a fresh baseplate. <c>done</c> runs on the
    /// render thread.
    /// </summary>
    public void NewMap(string name, Action<Exception?> done)
    {
        ArgumentNullException.ThrowIfNull(done);

        Host.EnqueueCommand(scene =>
        {
            try
            {
                // Same reset as OpenMap.
                SceneManager.OnSceneReplaced();
                Editor?.OnSceneReplaced();

                var empty = new MapDocument();
                empty.Scene.Name = string.IsNullOrWhiteSpace(name) ? "Scene" : name;
                MapSceneBinder.ApplyTo(empty, scene);

                SceneManager.PopulateBaseplate(scene);
                scene.RebuildStaticWorld(_renderer);

                done(null);
            }
            catch (Exception ex) when (ex is MapFormatException or IOException)
            {
                done(ex);
            }
        });
    }

    /// <summary>
    /// Stops the engine and waits for the render thread. Safe to call twice.
    /// </summary>
    public void Stop()
    {
        if (!_engine.IsRunning)
            return;

        bool clean = _engine.Stop();
        if (!clean)
            _logger.LogError("The render thread ended on an exception; see the log above");
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();
}
