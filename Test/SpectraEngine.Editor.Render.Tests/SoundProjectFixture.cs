using Spectra.Kitchen.Tests;

using SpectraEngine.Core.Entities;

namespace SpectraEngine.Editor.Render.Tests;

// A project's Assets folder with sounds among other files, and a sound class
// shaped as point_sound, for the sheets that show sounds.
internal sealed class SoundProjectFixture : IDisposable
{
    public const string ClassName = "test_sound";
    public const string DoorOpen = "Sounds/door_open.wav";
    public const string DoorClose = "Sounds/door_close.wav";

    // The sounds, in the order a list shows them.
    public static readonly string[] Sounds =
    [
        "Sounds/ambience/wind.wav", DoorClose, DoorOpen, "Sounds/guard_hey.wav", "Sounds/lift_hum.wav",
    ];

    public SoundProjectFixture()
    {
        Write(DoorOpen, TempProject.Wav(frames: 300, sampleRate: 44_100));
        Write(DoorClose, TempProject.Wav(frames: 300, sampleRate: 44_100));
        Write("Sounds/lift_hum.wav", TempProject.Wav(frames: 400, channels: 2, loopStart: 20, loopEnd: 379));
        Write("Sounds/guard_hey.wav", TempProject.Wav(frames: 500));
        Write("Sounds/ambience/wind.wav", TempProject.Wav(frames: 200, channels: 2));
        Write("Sounds/guard_hey.en.vtt", "WEBVTT\n"u8.ToArray());
        Write("Sounds/guard_hey.markers.txt", "0.2\they\n"u8.ToArray());
        Write("Sounds/brick.png", TempProject.Png());
        Write("Sounds/wall.spectramat", "shader lit\n"u8.ToArray());
        Write("Sounds/crate.obj", "o crate\n"u8.ToArray());
    }

    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "spectra-sound-sheets-" + Guid.NewGuid().ToString("N"));

    // The folder that holds the sounds beside files of other kinds.
    public string SoundsFolder => Path.Combine(Root, "Sounds");

    // Learned from .sentdef bytes, as the editor learns any class.
    public static EntitySchemaCatalog Schemas { get; } = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema(ClassName, "Sound", "Sound", keyvalues:
        [
            Setting("sound", "Sound", KeyvalueType.AssetSound, ""),
            Setting("volume", "Volume", KeyvalueType.Float, "1"),
            Setting("pitch", "Pitch", KeyvalueType.Float, "1"),
            Setting("mindistance", "Minimum distance", KeyvalueType.Distance, "2"),
            Setting("maxdistance", "Maximum distance", KeyvalueType.Distance, "30"),
            Setting("looped", "Looped", KeyvalueType.Bool, "0"),
            Setting("startplaying", "Start playing", KeyvalueType.Bool, "0"),
        ]),
    ]));

    // An engine that takes every request a play button sends.
    public static Func<string, bool> Taking(List<string> asked) => path =>
    {
        asked.Add(path);
        return true;
    };

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp folder is not a test failure.
        }
    }

    private static KeyvalueDescriptor Setting(string name, string display, KeyvalueType type, string value) =>
        new(name, display, "", value, type, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
            KeyvalueDescriptor.NoChoices);

    private void Write(string relative, byte[] bytes)
    {
        string full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full).ShouldNotBeNull());
        File.WriteAllBytes(full, bytes);
    }
}
