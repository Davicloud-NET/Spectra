namespace SpectraEngine.Bsp.Tests;

// One sine in a test sound. Amplitude 1 is full scale.
internal readonly record struct TestTone(double Hz, double Amplitude, double Phase = 0);
