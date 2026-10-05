using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

// A scene with fixture entity classes and an editor over it. The classes
// exist only as .sentdef bytes, the way the editor learns any class.
internal sealed class EntityEditRig
{
    public const string Door = "test_door";
    public const string Trigger = "test_trigger";
    public const string Relay = "test_relay";
    public const string Marker = "test_marker";

    public EntityEditRig()
    {
        Scene = new Scene("entities")
        {
            EntitySchemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
            [
                new EntitySchema(Door, placement: EntityPlacement.Brush),
                new EntitySchema(Trigger, placement: EntityPlacement.Volume),
                new EntitySchema(Relay, placement: EntityPlacement.Abstract),
                new EntitySchema(Marker, placement: EntityPlacement.Point),
            ])),
        };

        Renderer = new CompilingRenderer();
        Renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));

        Host = new SceneEditorHost(
            NullLoggerFactory.Instance, Scene, Renderer,
            new InputManager(NullLogger<InputManager>.Instance));
    }

    public Scene Scene { get; }
    public CompilingRenderer Renderer { get; }
    public SceneEditorHost Host { get; }

    public static Brush Box() => Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

    public SceneNode AddBlock(string name, SceneNode? parent = null)
    {
        SceneNode node = (parent ?? Scene.Root).CreateChild(name);
        node.Brush = Box();
        return node;
    }

    public SceneNode AddPart(string name, SceneNode? parent = null)
    {
        SceneNode node = (parent ?? Scene.Root).CreateChild(name);
        node.BrushKind = BrushKind.Part;
        node.Brush = Box();
        return node;
    }

    public SceneNode AddCut(string name, SceneNode? parent = null)
    {
        SceneNode node = (parent ?? Scene.Root).CreateChild(name);
        node.Brush = Box().WithOperation(BrushOperation.Subtractive);
        return node;
    }

    public EntityEditReport Make(string className, params SceneNode[] selection)
    {
        Scene.Selection.SetRange(selection);
        return Host.MakeEntity(className);
    }

    public EntityEditReport Remove(params SceneNode[] selection)
    {
        Scene.Selection.SetRange(selection);
        return Host.RemoveEntity();
    }
}
