namespace Server.Tests.Support;

// Concurrent allocation pressure contaminates the thread-counter measurement.
[CollectionDefinition("Allocation measurements", DisableParallelization = true)]
public sealed class AllocationMeasurementCollection;
