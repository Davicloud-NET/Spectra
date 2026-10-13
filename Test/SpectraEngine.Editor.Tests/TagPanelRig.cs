using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Undo;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

// The Properties panel's model over a real scene: what the panel posts is
// applied by the editors the engine uses, and what the engine would publish
// is captured from the nodes and handed back.
internal sealed class TagPanelRig
{
    private TagPanelInfo? _tags;
    private AttributePanelInfo? _attributes;

    public TagPanelRig(EntitySchemaCatalog? schemas, params SceneNode[] selected)
    {
        Scene = new Scene("panel") { EntitySchemas = schemas };
        foreach (SceneNode node in selected)
            Scene.Root.AddChild(node);

        Undo = new UndoStack(Scene);
        Selected = [.. selected];
        Panel = new PropertyPanelModel(
            _ => { }, _ => { }, _ => { }, null,
            edit =>
            {
                TagEdits.Add(edit);
                TagEditor.Apply(Undo, Selected, edit);
            },
            edit =>
            {
                AttributeEdits.Add(edit);
                AttributeEditor.Apply(Undo, Selected, edit);
            });

        Publish();
    }

    public Scene Scene { get; }

    public UndoStack Undo { get; }

    // The selection. A test may change it and publish.
    public List<SceneNode> Selected { get; }

    public PropertyPanelModel Panel { get; }

    public List<TagEdit> TagEdits { get; } = [];

    public List<AttributeEdit> AttributeEdits { get; } = [];

    public TagsModel Tags => Panel.Tags;

    public AttributesModel Attributes => Panel.Attributes;

    // Hands the panel what the engine would publish now.
    public void Publish()
    {
        _tags = TagPanelInfo.Capture(Selected, _tags);
        _attributes = AttributePanelInfo.Capture(Selected, Scene.EntitySchemas, _attributes);
        Panel.Apply([], Selected.Count, null, _tags, _attributes);
    }

    // Types into a field as a person does, presses Enter, and lets the
    // engine answer.
    public void Type(PropertyFieldModel field, string text)
    {
        field.BeginEdit();
        field.Text = text;
        field.Commit();
        Publish();
    }

    public AttributeRowModel Row(string name) => Attributes.Rows.Single(row => row.Name == name);
}
