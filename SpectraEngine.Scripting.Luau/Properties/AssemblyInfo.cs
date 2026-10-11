using System.Runtime.CompilerServices;

// With marshalling off, a type that is not blittable in a P/Invoke is a
// compile error instead of a wrong layout at run time.
[assembly: DisableRuntimeMarshalling]

// Both check the raw binding against the real library.
[assembly: InternalsVisibleTo("SpectraEngine.Scripting.Tests")]
[assembly: InternalsVisibleTo("SpectraEngine.Scripting.CrashCases")]
