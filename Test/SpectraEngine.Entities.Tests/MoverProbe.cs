using SpectraEngine.Core.Entities;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

// Owns a LinearMover and nothing else, so a test can drive the mover with no
// door around it. Hand-written, so a failure cannot be the generator's.
internal sealed class MoverProbe : Entity
{
    public const string WireName = "test_mover";

    private Vector3 _direction = Vector3.UnitY;
    private float _distance = 2f;
    private float _speed = 2f;

    public MoverProbe() => Mover = new LinearMover(this);

    public LinearMover Mover { get; }

    public override bool ParseKeyValue(string key, string value)
    {
        switch (key)
        {
            case "direction": return KeyvalueWire.TryParseVec3(value, out _direction);
            case "distance": return KeyvalueWire.TryParseFloat(value, out _distance);
            case "speed": return KeyvalueWire.TryParseFloat(value, out _speed);
            default: return false;
        }
    }

    protected override void OnSpawn() => Mover.SetTravel(_direction, _distance, _speed);
}
