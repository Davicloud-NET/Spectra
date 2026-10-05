namespace SpectraEngine.Core.Audio;

/// <summary>
/// A named moment in a sound, for gameplay to react to when a playing sound
/// reaches it.
/// </summary>
/// <param name="Frame">
/// Sample frames from the start of the sound. A marker at the sound's frame
/// count sits at its very end.
/// </param>
/// <param name="Name">What the sound's author called it.</param>
public readonly record struct AudioMarker(long Frame, string Name);
