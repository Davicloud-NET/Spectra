namespace OpenAlFilterSpike;

/// <summary>One sine in a test signal. Amplitude 1 is full scale, the phase is in radians.</summary>
internal readonly record struct Tone(double Hz, double Amplitude, double Phase = 0);
