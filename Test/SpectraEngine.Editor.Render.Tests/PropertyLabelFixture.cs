using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;
using System.Numerics;

namespace SpectraEngine.Editor.Render.Tests;

// Things to select whose settings have long names and short ones, and a
// Properties panel that shows them at a width a test picks.
internal static class PropertyLabelFixture
{
    // The inspector column's default, its expanded preset and its minimum.
    public const double UsualWidth = 280;
    public const double ExpandedWidth = 308;
    public const double NarrowestWidth = 220;

    private const string SoundClass = "test_sound";
    private const string SpawnerClass = "test_spawner";

    // Learned from .sentdef bytes, as the editor learns any class.
    private static readonly EntitySchemaCatalog Schemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        // point_sound's settings, with the names it ships and a line of its help.
        new EntitySchema(SoundClass, "Sound", "Sound", keyvalues:
        [
            Setting("sound", "Sound", KeyvalueType.AssetSound, "", "The sound file to play."),
            Setting("volume", "Volume", KeyvalueType.Float, "1",
                "How loud the sound is. 1 is the file as it is, 0 is silent."),
            Setting("pitch", "Pitch", KeyvalueType.Float, "1",
                "How fast the sound plays. 2 is twice as fast and an octave higher."),
            Setting("mindistance", "Minimum distance", KeyvalueType.Distance, "2",
                "The sound is at full volume inside this distance."),
            Setting("maxdistance", "Maximum distance", KeyvalueType.Distance, "30",
                "The sound is silent beyond this distance."),
            Setting("looped", "Looped", KeyvalueType.Bool, "0", "The sound repeats until something stops it."),
            Setting("startplaying", "Start playing", KeyvalueType.Bool, "0",
                "The sound plays when the level starts."),
            Setting("placed", "Placed", KeyvalueType.Bool, "1", "The sound is heard from where it stands."),
            Setting("fades", "Fades with distance", KeyvalueType.Bool, "1",
                "The sound gets quieter with distance."),
            Setting("walls", "Muffled by walls", KeyvalueType.Bool, "1",
                "Walls in the way make the sound quieter and duller."),
            Setting("doppler", "Doppler", KeyvalueType.Bool, "1",
                "The pitch rises while the sound and the listener close in."),
        ]),

        // Names as long as a script's author may write them, one for each
        // editor, and none with any help.
        new EntitySchema(SpawnerClass, "Spawner", "Logic", keyvalues:
        [
            Setting("interval", "Seconds between two spawns", KeyvalueType.Float, "2.5"),
            Setting("reach", "Farthest a spawned thing may land", KeyvalueType.Distance, "12"),
            Setting("light", "Warning light colour", KeyvalueType.Color, "1 0.5 0.1"),
            Setting("empty", "Entity to tell when it runs dry", KeyvalueType.TargetName, "Alarm"),
            Setting("sound", "Sound of a spawn", KeyvalueType.AssetSound, "Sounds/door_open.wav"),
            Setting("label", "Name shown to the player", KeyvalueType.String, "Crates"),
            Setting("paused", "Starts paused until told to run", KeyvalueType.Bool, "0"),
            Setting("limit", "Limit", KeyvalueType.Int, "8"),
        ]),
    ]));

    public static SceneNode Sound() => Entity(SoundClass);

    public static SceneNode Spawner() => Entity(SpawnerClass);

    // Every label of a light is short.
    public static SceneNode Lamp() => new("Lamp") { Light = new Light { Kind = LightKind.Point, Range = 6f } };

    // What is selected most of the time. "Seen by queries" is its longest label.
    public static SceneNode Part() =>
        new("Crate") { Brush = Brush.CreateBox(-Vector3.One, Vector3.One), BrushKind = BrushKind.Part };

    // Call on the session's UI thread. The window is 2x, as the sheets are.
    // The list takes the edits the panel posts.
    public static (Window Window, PropertiesPanel Panel, List<PropertyEdit> Edits) Open(
        double width, params SceneNode[] selection)
    {
        var edits = new List<PropertyEdit>();
        var panelModel = new PropertyPanelModel(edits.Add, _ => { }, _ => { }) { Schemas = Schemas };

        var panel = new PropertiesPanel { DataContext = new ShellModel { Properties = panelModel } };
        Select(panel, selection);

        var window = new Window { Content = panel, Width = width, Height = 700 };
        window.SetRenderScaling(2.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, panel, edits);
    }

    // Publishes the rows of a selection to the panel, as the engine does.
    public static void Select(PropertiesPanel panel, params SceneNode[] selection)
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(selection, rows, Schemas);

        var model = (ShellModel)panel.DataContext.ShouldNotBeNull();
        model.Properties.ShouldNotBeNull().Apply(rows, selection.Length);
        Dispatcher.UIThread.RunJobs();
    }

    private static SceneNode Entity(string className) => new(className) { Entity = new EntityData(className) };

    private static KeyvalueDescriptor Setting(
        string name, string display, KeyvalueType type, string value, string tooltip = "") =>
        new(name, display, tooltip, value, type, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
            KeyvalueDescriptor.NoChoices);
}
