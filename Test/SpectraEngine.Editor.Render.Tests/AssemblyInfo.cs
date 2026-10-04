using Xunit;

// One headless session and one UI thread for the whole assembly. Parallel
// collections would run two layout passes on the same dispatcher.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
