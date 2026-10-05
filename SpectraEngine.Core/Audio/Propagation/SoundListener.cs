using System.Numerics;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>Where the listener is and which way it faces, in world space.</summary>
public readonly record struct SoundListener(Vector3 Position, Vector3 Forward, Vector3 Up);
