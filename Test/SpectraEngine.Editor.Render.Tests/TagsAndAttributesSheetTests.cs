using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;

using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Undo;
using SpectraEngine.Editor.Shell;
using System.Numerics;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// Rasterises the Tags and Attributes sections of the property panel so a
/// person can look at them: for one node, for several, and with what the
/// rules refuse marked where it was typed.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class TagsAndAttributesSheetTests(RibbonSession session)
{
    [Fact]
    public void One_nodes_tags_and_an_attribute_of_every_kind_rasterise_into_a_sheet()
    {
        session.On(() =>
        {
            using var sheet = new Sheet(null, Brick());

            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("sectionhead")).Select(text => text.Text)
                .ShouldBe(["Transform", "Tags", "Attributes"]);
            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("tagname")).Select(text => text.Text)
                .ShouldBe(["Lava", "Hot stuff", "Checkpoint 3"]);
            sheet.Shown<ComboBox>().Select(box => box.SelectedItem).ShouldBe(
                ["Text", "Number", "True or false", "Three numbers", "Color", "Position and rotation", "Text"]);
            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("rejection")).ShouldBeEmpty();

            sheet.Save("properties-tags-one@2x.png");
        });
    }

    [Fact]
    public void Several_nodes_show_a_tag_some_have_as_partial_and_values_that_differ_as_mixed()
    {
        session.On(() =>
        {
            SceneNode a = Brick();
            var b = new SceneNode("Pool")
            {
                Tags = TagSet.Of("Lava"),
                Attributes = AttributeSet.Empty
                    .With("Damage", MemberValue.Of(40d))
                    .With("Push", MemberValue.Of(new Vector3(0f, 3f, -1f)))
                    .With("Label", MemberValue.Of(7d)),
            };
            using var sheet = new Sheet(null, a, b, new SceneNode("Ember") { Tags = TagSet.Of("Lava") });

            sheet.Shown<Border>().Where(chip => chip.Classes.Contains("tagchip"))
                .Select(chip => chip.Classes.Contains("partial")).ShouldBe([false, true, true]);
            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("micro") && text.Text is { Length: > 0 })
                .Select(text => text.Text).ShouldContain("1 of 3");
            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("rownote")).Select(text => text.Text)
                .ShouldContain("On 2 of 3. A new value goes on all 3.");
            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("rownote")).Select(text => text.Text)
                .ShouldContain("The selected nodes hold values of different kinds here. Pick a kind to give them all one.");

            sheet.Save("properties-tags-several@2x.png");
        });
    }

    [Fact]
    public void What_the_rules_refuse_is_marked_where_it_was_typed_with_the_reason_under_it()
    {
        session.On(() =>
        {
            SceneNode door = Brick();
            door.Name = "Door";
            door.Entity = new EntityData("door");
            door.Attributes = door.Attributes.With("speed", MemberValue.Of(9d));
            using var sheet = new Sheet(Doors(), door);

            sheet.Type(sheet.Panel.Tags.AddField, new string('x', TagLimits.MaxBytes + 1));
            sheet.Type(sheet.Panel.Attributes.NewNameField, "two words");
            sheet.Type(sheet.Panel.Attributes.Rows.Single(row => row.Name == "Damage").Cells[0], "a lot");
            sheet.Type(sheet.Panel.Attributes.Rows.Single(row => row.Name == "Label").NameField, "Damage");

            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("rejection")).Select(text => text.Text)
                .ShouldBe(
                [
                    "Not added: A tag of 257 bytes is too long. A tag is 1 to 256 bytes of text.",
                    "Not renamed: There is an attribute called 'Damage' already.",
                    "Not applied: expected a number.",
                    "Not added: 'two words' is not a valid attribute name. An attribute's name is 1 to 100 " +
                    "letters, digits and underscores, and does not start with RBX.",
                ]);
            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("warn")).Select(text => text.Text).ShouldBe(
                ["The entity has a setting called 'speed', and scripts read that. Give this attribute another name."]);
            sheet.Shown<Border>().Count(cell => cell.Classes.Contains("cell") && cell.Classes.Contains("invalid"))
                .ShouldBe(4);
            sheet.Undo.UndoCount.ShouldBe(0, "nothing that was refused reached the level");

            sheet.Save("properties-tags-refused@2x.png");

            // The sheet shows what was asserted: nothing took it back while it was drawn.
            sheet.Panel.Attributes.NewNameField.Text.ShouldBe("two words");
            sheet.Shown<TextBlock>().Count(text => text.Classes.Contains("rejection")).ShouldBe(4);
            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("rejection"))
                .ShouldAllBe(text => text.Bounds.Height > 0);
        });
    }

    [Fact]
    public void Enter_in_the_tag_field_adds_a_chip_and_the_remove_button_takes_it_off()
    {
        session.On(() =>
        {
            SceneNode brick = Brick();
            using var sheet = new Sheet(null, brick);

            sheet.Type(sheet.Panel.Tags.AddField, "Deep");
            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("tagname")).Select(text => text.Text)
                .ShouldBe(["Lava", "Hot stuff", "Checkpoint 3", "Deep"]);

            sheet.Panel.Tags.Remove(sheet.Panel.Tags.Chips[0]);
            sheet.Publish();
            brick.Tags.ShouldBe(["Hot stuff", "Checkpoint 3", "Deep"]);
            sheet.Shown<Border>().Count(chip => chip.Classes.Contains("tagchip")).ShouldBe(3);
        });
    }

    [Fact]
    public void While_the_level_plays_the_two_sections_cannot_be_edited_and_say_why()
    {
        session.On(() =>
        {
            using var sheet = new Sheet(null, Brick());
            TextBox tagField = sheet.Shown<TextBox>().Single(box => box.PlaceholderText == "Add a tag");
            tagField.IsEffectivelyEnabled.ShouldBeTrue();
            sheet.Shown<TextBlock>().ShouldNotContain(text => text.Text != null && text.Text.Contains("after Stop"));

            sheet.Play(true);

            tagField.IsEffectivelyEnabled.ShouldBeFalse();
            sheet.Shown<TextBlock>().ShouldContain(text => text.Text != null && text.Text.Contains("after Stop"));
            sheet.Save("properties-tags-playing@2x.png");

            sheet.Play(false);
            tagField.IsEffectivelyEnabled.ShouldBeTrue();
        });
    }

    [Theory]
    [InlineData("tag:ice", "Nothing here has a tag with", "ice")]
    [InlineData("tag:", "Nothing here has a tag.", "")]
    [InlineData("ice", "Nothing here is called", "ice")]
    public void The_scene_panel_says_what_a_tag_filter_found_nothing_of(string filter, string words, string named)
    {
        session.On(() =>
        {
            var shell = new ShellModel
            {
                Tree = new SceneTreeModel(new EngineHost(NullLogger.Instance), NullLogger.Instance),
                FilterText = filter,
            };

            shell.NoMatchLabel.ShouldStartWith(words);
            shell.NoMatchLabel.ShouldContain(named);
        });
    }

    // A part with three tags and an attribute of every kind.
    private static SceneNode Brick() => new("Brick")
    {
        Tags = TagSet.Of("Lava", "Hot stuff", "Checkpoint 3"),
        Attributes = AttributeSet.Empty
            .With("Label", MemberValue.Of("hot"))
            .With("Damage", MemberValue.Of(25d))
            .With("Deadly", MemberValue.Of(true))
            .With("Push", MemberValue.Of(new Vector3(0f, 12.5f, -1f)))
            .With("Glow", MemberValue.Of(new Color3(1f, 0.5f, 0f)))
            .With("Rest", MemberValue.Of(new CFrame(
                new Vector3(1f, 2f, 3f), EulerAngles.FromDegrees(new Vector3(0f, 90f, 0f)).ToQuaternion()))),
    };

    private static EntitySchemaCatalog Doors() => EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema("door", "Door", "Doors", keyvalues:
        [
            new("speed", "Speed", "", "100", KeyvalueType.Float, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
                KeyvalueDescriptor.NoChoices),
        ]),
    ]));

    // The property panel over a real scene, at the inspector's expanded
    // width and tall enough to show both sections with nothing scrolled off.
    private sealed class Sheet : IDisposable
    {
        private readonly Scene _scene;
        private readonly SceneNode[] _selected;
        private readonly Window _window;
        private readonly PropertiesPanel _view;
        private readonly ShellModel _shell;
        private TagPanelInfo? _tags;
        private AttributePanelInfo? _attributes;
        private bool _isPlaying;
        private long _frame;

        public Sheet(EntitySchemaCatalog? schemas, params SceneNode[] selected)
        {
            _scene = new Scene("sheet") { EntitySchemas = schemas };
            foreach (SceneNode node in selected)
                _scene.Root.AddChild(node);

            _selected = selected;
            Undo = new UndoStack(_scene);
            Panel = new PropertyPanelModel(
                _ => { }, _ => { }, _ => { }, null,
                edit => TagEditor.Apply(Undo, _selected, edit),
                edit => AttributeEditor.Apply(Undo, _selected, edit))
            {
                Schemas = schemas,
            };

            _shell = new ShellModel { Properties = Panel };
            _view = new PropertiesPanel { DataContext = _shell };
            _window = new Window { Content = _view, Width = PropertyLabelFixture.ExpandedWidth, Height = 1180 };
            _window.SetRenderScaling(2.0);
            _window.Show();
            Publish();
        }

        public PropertyPanelModel Panel { get; }

        public UndoStack Undo { get; }

        // Hands the shell a snapshot as the engine would publish it now.
        public void Publish()
        {
            var rows = new List<PropertyRow>();
            NodeInspector.Describe(_selected, rows, _scene.EntitySchemas);
            _tags = TagPanelInfo.Capture(_selected, _tags);
            _attributes = AttributePanelInfo.Capture(_selected, _scene.EntitySchemas, _attributes);

            _shell.ApplySnapshot(new FrameSnapshot
            {
                FrameNumber = ++_frame,
                SelectedIds = [.. _selected.Select(node => node.Id)],
                SelectionProperties = rows,
                SelectionTags = _tags,
                SelectionAttributes = _attributes,
                IsPlaying = _isPlaying,
            });
            Dispatcher.UIThread.RunJobs();
        }

        // Starts or stops the level, as far as the shell is told.
        public void Play(bool isPlaying)
        {
            _isPlaying = isPlaying;

            // The shell holds its own word for a few snapshots before the engine's wins.
            for (int i = 0; i < 12; i++)
                Publish();
        }

        public void Type(PropertyFieldModel field, string text)
        {
            field.BeginEdit();
            field.Text = text;
            field.Commit();
            Publish();
        }

        public T[] Shown<T>() where T : Control =>
            [.. _view.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible)];

        public void Save(string name)
        {
            // More than one tick: the frame of the first can be the one from
            // before the last change was handed to the renderer.
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            }

            WriteableBitmap frame = _window.CaptureRenderedFrame().ShouldNotBeNull();
            frame.PixelSize.Width.ShouldBe((int)(PropertyLabelFixture.ExpandedWidth * 2));

            Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
            frame.Save(Path.Combine(RibbonSheetTests.OutputDirectory, name), new PngBitmapEncoderOptions());
        }

        public void Dispose() => _window.Close();
    }
}
