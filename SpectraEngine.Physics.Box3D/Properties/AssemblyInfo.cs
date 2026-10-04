using System.Runtime.CompilerServices;


// Not a performance knob: with marshalling off, a non-blittable struct in a
// P/Invoke is a compile error instead of a wrong layout at runtime. A C bool
// is one byte, so those fields are byte here, never bool.
[assembly: DisableRuntimeMarshalling]

// The tests check the raw binding against the real library.
[assembly: InternalsVisibleTo("SpectraEngine.Physics.Tests")]
