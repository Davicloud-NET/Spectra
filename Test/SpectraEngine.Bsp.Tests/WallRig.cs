using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

// A wall propagation with its own clock, a listener and a list of sounds,
// asked a frame at a time the way the presenter asks it.
internal sealed class WallRig
{
    public const float FrameSeconds = 1f / 60f;

    public static readonly MaterialRef Concrete = MaterialRegistry.Intern("Materials/span_concrete.spectramat");

    private readonly List<SoundQuery> _sounds = [];
    private SoundQuery[] _queries = [];
    private SoundPaths[] _paths = [];

    public WallRig(
        ISoundObstacles world, IAcousticMaterials? materials = null, WallPropagationSettings? settings = null)
    {
        Walls = new WallPropagation(world, materials ?? new FakeAcousticMaterials(), settings, Clock);
    }

    // Over a real scene, with the span level's materials made of what they
    // are named for.
    public WallRig(SpanLevel level, WallPropagationSettings? settings = null)
        : this(new SceneSoundObstacles(() => level.Scene), SpanMaterials(), settings)
    {
    }

    public ManualClock Clock { get; } = new();

    public WallPropagation Walls { get; }

    public Vector3 Listener { get; set; }

    public int Count => _sounds.Count;

    public static FakeAcousticMaterials SpanMaterials() => new FakeAcousticMaterials()
        .Add(SpanLevel.Brick, AcousticPresets.Brick)
        .Add(SpanLevel.Plaster, AcousticPresets.Plaster)
        .Add(SpanLevel.Wood, AcousticPresets.Wood)
        .Add(Concrete, AcousticPresets.Concrete);

    // Adds a sound and returns its place in the list.
    public int Add(Vector3 position, SceneNode? body = null, float minDistance = 2f, float maxDistance = 30f)
    {
        _sounds.Add(new SoundQuery(position, minDistance, maxDistance) { Body = body });
        return _sounds.Count - 1;
    }

    // Adds a sound that sits on a node and is where the node is.
    public int Add(SceneNode body) => Add(body.WorldPosition, body);

    public void Move(int sound, Vector3 position) => _sounds[sound] = _sounds[sound] with { Position = position };

    public void RemoveAt(int sound) => _sounds.RemoveAt(sound);

    // One frame: the clock moves on and every sound is asked about.
    public void Frame(int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            Clock.Advance(FrameSeconds);

            if (_queries.Length != _sounds.Count)
            {
                _queries = new SoundQuery[_sounds.Count];
                _paths = new SoundPaths[_sounds.Count];
            }

            _sounds.CopyTo(_queries);
            Walls.Resolve(new SoundListener(Listener, -Vector3.UnitZ, Vector3.UnitY), _queries, _paths);
        }
    }

    // What the last frame made of a sound. Silent for one with no path.
    public SoundPath Heard(int sound) =>
        _paths[sound].Count > 0 ? _paths[sound][0] : new SoundPath(_sounds[sound].Position, 0f, 1f);

    public bool HasPath(int sound) => _paths[sound].Count > 0;

    // The same sound by distance alone.
    public SoundPath Direct(int sound)
    {
        var results = new SoundPaths[1];
        new DirectPropagation().Resolve(
            new SoundListener(Listener, -Vector3.UnitZ, Vector3.UnitY), [_sounds[sound]], results);
        return results[0][0];
    }

    // What the walls alone left of a sound: the two factors on top of distance.
    public AcousticGains Through(int sound)
    {
        SoundPath heard = Heard(sound);
        return new AcousticGains(heard.Gain / Direct(sound).Gain, heard.GainHf);
    }
}
