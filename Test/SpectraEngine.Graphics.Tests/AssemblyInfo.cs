using Xunit;

// The GL and D3D collections each create a graphics device. Created in
// parallel they race: D3D11CreateDevice reports success and returns no device,
// about one run in four. The engine only ever creates one device per process.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
