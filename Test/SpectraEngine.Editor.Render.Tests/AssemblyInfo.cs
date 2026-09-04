using Xunit;

// ONE dispatcher, one Application, for the whole assembly. The headless session
// owns a UI thread and every test here shows a Window on it; xUnit running two
// collections in parallel would put two layout passes on one dispatcher, which
// is the shape of flake SpectraEngine.Graphics.Tests already serialises its
// device creation to avoid.
//
// The cost is small and known: the styles are parsed once per assembly rather
// than once per test, and these tests create a window, measure it and close it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
