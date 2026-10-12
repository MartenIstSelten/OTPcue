using Xunit;

// Suites change process-wide language or use shared desktop state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
