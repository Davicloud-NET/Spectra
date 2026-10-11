using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;

using Spectra.Kitchen.Tests;

using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Logic;
using SpectraEngine.Editor.Tests;

using static SpectraEngine.Editor.Tests.Logic.LogicFixture;
using static SpectraEngine.Editor.Tests.Logic.LogicPlayFixture;

using Path = Avalonia.Controls.Shapes.Path;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// Every place the shell shows the icon of an entity, a light or a file, with
/// one of each on a sheet: the scene tree, the Logic view's cards, the entity
/// lists and the Content panel. For a person to compare across sheets.
/// </summary>
// Every sheet takes its classes from the engine's own, through .sentdef
// bytes. A class added to the engine shows up here with nothing to edit.
[Collection(RibbonSessionCollection.Name)]
public sealed class IconSheetTests(RibbonSession session)
{
    private const string Stranger = "from_another_game";

    private static EntitySchemaCatalog Classes => BuiltinClasses.Catalog;

    [Fact]
    public void The_scene_tree_shows_one_node_of_every_kind_and_every_class()
    {
        session.On(() =>
        {
            (SceneTreeModel tree, List<SceneChange> nodes) = EveryKindOfNode();
            var panel = new ScenePanel { DataContext = new ShellModel { Tree = tree } };
            var window = new Window { Content = panel, Width = 300, Height = 72 + (nodes.Count * 24) };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // The glyph a row draws, in the tint a row draws it in.
            Path[] glyphs = [.. panel.GetVisualDescendants().OfType<Path>().Where(path => path.Classes.Contains("kindtint"))];
            glyphs.Length.ShouldBe(nodes.Count, "every row should be on the sheet");

            foreach (Path glyph in glyphs)
            {
                var row = glyph.DataContext.ShouldBeOfType<SceneTreeNode>();
                glyph.Data.ShouldBeSameAs(EditorIcons.Glyph(row.Look), row.Name);
                glyph.Data.ShouldNotBeNull(row.Name);
                glyph.Stroke.ShouldBeSameAs(EditorIcons.Brush(row.Look), row.Name);
                glyph.Stroke.ShouldNotBeNull(row.Name);
            }

            Save(window, "icons-scene-tree@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void The_Logic_view_shows_a_card_for_every_class()
    {
        session.On(() =>
        {
            LogicEntityInfo[] entities = EveryClassOfEntity();
            var model = new LogicViewModel { Schemas = Classes, Mode = LogicScopeMode.WholeLevel };

            // Selected, so the ones with no wire are shown too.
            model.Apply(Snapshot(Level(entities), null, [.. entities.Select(entity => entity.NodeId)]));

            (LogicView view, Window window) = LogicSheetTests.Open(model, 1500, 760);

            try
            {
                LogicScene scene = model.Scene.ShouldNotBeNull();
                scene.Cards.Count.ShouldBeGreaterThanOrEqualTo(entities.Length);

                foreach (LogicEntityInfo entity in entities)
                {
                    scene.Cards.Single(card => card.Card.NodeId == entity.NodeId).Card.Look
                        .ShouldBe(EditorIcons.ForEntity(Classes, entity.ClassName), entity.ClassName);
                }

                model.FitCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                Save(window, "icons-logic-cards@2x.png");
                LogicTheme.Missing.ShouldBeEmpty("a card asked the theme for a key it does not have");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false, "icons-entity-menu@2x.png")]
    [InlineData(true, "icons-make-entity-menu@2x.png")]
    public void An_entity_list_shows_every_class_with_its_icon(bool forMake, string sheet)
    {
        session.On(() =>
        {
            List<EntityInsertItem> all = EntityInsertMenu.Build(Classes);
            List<EntityInsertItem> items = forMake ? EntityInsertMenu.MadeFromGeometry(all) : all;
            var list = new MenuFlyoutPresenter();

            EntityInsertMenu.Fill(list.Items, items, forMake, _ => { });

            var window = new Window { Content = list, SizeToContent = SizeToContent.WidthAndHeight };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            MenuItem[] rows = [.. list.Items.OfType<MenuItem>()];
            rows.Length.ShouldBe(items.Count);
            if (!forMake)
                rows.Length.ShouldBe(Classes.Count);

            for (int i = 0; i < rows.Length; i++)
            {
                var glyph = rows[i].Icon.ShouldBeOfType<Path>();
                glyph.Data.ShouldBeSameAs(EditorIcons.Glyph(items[i].Look), items[i].ClassName);
                glyph.Data.ShouldNotBeNull(items[i].ClassName);
                glyph.Stroke.ShouldBeSameAs(EditorIcons.Brush(items[i].Look), items[i].ClassName);
                glyph.IsEffectivelyVisible.ShouldBeTrue(items[i].ClassName);
            }

            Save(window, sheet);
            window.Close();
        });
    }

    [Theory]
    [InlineData(ContentViewMode.List, "icons-content-list@2x.png")]
    [InlineData(ContentViewMode.Grid, "icons-content-grid@2x.png")]
    public void The_content_panel_shows_a_file_of_every_kind(ContentViewMode view, string sheet)
    {
        string root = EveryKindOfFile();

        try
        {
            session.On(async () =>
            {
                (Window window, ContentPanel panel, ShellModel model) =
                    await ContentPanelHarness.OpenAsync(root, System.IO.Path.Combine(root, "Icons"), view);
                ContentBrowserModel browser = model.Content.ShouldNotBeNull();

                browser.Entries.Select(entry => entry.Kind).ShouldBe(Enum.GetValues<ContentKind>(), ignoreOrder: true);
                foreach (ContentEntry entry in browser.Entries)
                {
                    entry.Icon.ShouldBeSameAs(EditorIcons.Glyph(EditorIcons.ForFile(entry.Kind)), entry.Name);
                    entry.Icon.ShouldNotBeNull(entry.Name);
                    entry.KindBrush.ShouldNotBeNull(entry.Name);
                    entry.HasThumbnail.ShouldBeFalse($"{entry.Name} should show its icon on this sheet");
                }

                Save(window, sheet);
                window.Close();
                browser.SetRoot(null);
            });
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A locked temp folder is not a test failure.
            }
        }
    }

    // One row of every node kind, every kind of light and every class, as the
    // engine would report them: ids and values in a snapshot.
    private static (SceneTreeModel Tree, List<SceneChange> Nodes) EveryKindOfNode()
    {
        Guid root = Guid.NewGuid();
        var nodes = new List<SceneChange>
        {
            new(SceneChangeKind.Added, root, Guid.Empty, "Level", -1, SceneNodeKind.Group),
        };

        void Add(string name, SceneNodeKind kind, string? entityClass = null, LightKind? light = null) => nodes.Add(
            new SceneChange(SceneChangeKind.Added, Guid.NewGuid(), root, name, nodes.Count - 1, kind)
            {
                EntityClass = entityClass,
                LightKind = light,
            });

        Add("Empty", SceneNodeKind.Empty);
        Add("Group", SceneNodeKind.Group);
        Add("Mesh", SceneNodeKind.Mesh);
        Add("Block", SceneNodeKind.BrushWorld);
        Add("Part", SceneNodeKind.BrushPart);
        Add("Cut", SceneNodeKind.BrushSubtractive);
        Add("Script", SceneNodeKind.Script);

        foreach (LightKind kind in Enum.GetValues<LightKind>())
            Add($"Light: {kind}", SceneNodeKind.Light, light: kind);

        foreach (EntitySchema schema in Classes.Schemas)
        {
            // A class made from geometry is a part to the engine.
            SceneNodeKind kind = EntityEditor.UsesGeometry(schema.Placement) ? SceneNodeKind.BrushPart : SceneNodeKind.Entity;
            Add(schema.ClassName, kind, schema.ClassName);
        }

        Add(Stranger, SceneNodeKind.Entity, Stranger);

        var tree = new SceneTreeModel(new EngineHost(NullLogger.Instance), NullLogger.Instance) { Schemas = Classes };
        tree.ApplyChanges(new FrameSnapshot { FrameNumber = 1, Changes = nodes });

        // One selected row, for the glyph a selected row wears.
        tree.ApplySelection([nodes[5].NodeId]);
        return (tree, nodes);
    }

    // One entity of every class and one of a class this build has no schema
    // for. The first is wired to a name nothing has and to whoever set it
    // off, so the two cards that are not entities are on the sheet too.
    private static LogicEntityInfo[] EveryClassOfEntity()
    {
        var entities = new List<LogicEntityInfo>();
        foreach (EntitySchema schema in Classes.Schemas)
        {
            string name = schema.DisplayName.Length > 0 ? schema.DisplayName : schema.ClassName;
            entities.Add(Entity(entities.Count + 1, name, schema.ClassName));
        }

        entities.Add(Entity(entities.Count + 1, "Stranger", Stranger));

        EntitySchema first = Classes.Schemas.First(schema => schema.Outputs.Count > 0);
        int at = entities.FindIndex(entity => entity.ClassName == first.ClassName);
        entities[at] = entities[at] with
        {
            Wires = [Wire(first.Outputs[0], "!activator", "Kill"), Wire(first.Outputs[0], "NobodyHasThisName", "Open")],
        };

        return [.. entities];
    }

    // A folder with one file of every kind the Content panel tells apart. The
    // picture is not one the shell can decode, so its row keeps the icon.
    private static string EveryKindOfFile()
    {
        string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "spectra-icon-sheets-" + Guid.NewGuid().ToString("N"));
        string folder = System.IO.Path.Combine(root, "Icons");
        Directory.CreateDirectory(System.IO.Path.Combine(folder, "Props"));

        File.WriteAllBytes(System.IO.Path.Combine(folder, "plaster.webp"), "not a picture"u8.ToArray());
        File.WriteAllBytes(System.IO.Path.Combine(folder, "wall.spectramat"), "shader lit\n"u8.ToArray());
        File.WriteAllBytes(System.IO.Path.Combine(folder, "crate.obj"), "o crate\n"u8.ToArray());
        File.WriteAllBytes(System.IO.Path.Combine(folder, "glow.spectrashade"), "shader Glow {}\n"u8.ToArray());
        File.WriteAllBytes(System.IO.Path.Combine(folder, "hum.wav"), TempProject.Wav(frames: 300));
        File.WriteAllBytes(System.IO.Path.Combine(folder, "spin.luau"), "print(\"spin\")\n"u8.ToArray());
        File.WriteAllBytes(System.IO.Path.Combine(folder, "notes.txt"), "notes\n"u8.ToArray());
        return root;
    }

    private static void Save(TopLevel surface, string fileName)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        WriteableBitmap frame = surface.GetLastRenderedFrame().ShouldNotBeNull("the sheet rasterised no frame at all");

        Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
        frame.Save(System.IO.Path.Combine(RibbonSheetTests.OutputDirectory, fileName), new PngBitmapEncoderOptions());

        LogicSheetTests.DistinctColours(frame).ShouldBeGreaterThan(8, "a sheet of one colour drew nothing");
    }
}
