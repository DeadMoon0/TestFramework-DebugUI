using Xunit;

// Collections run one at a time rather than in parallel.
//
// Several tests here drive real timeline runs, and the framework reads its transport configuration
// from environment variables - the pipe name, the journal root - which are process-wide. Two
// collections running at once would overwrite each other's settings, so a run would attach to
// another test's pipe server or write its journal into a directory nobody is looking at.
//
// The failures that produces are the worst kind: they depend on timing, they move when the suite is
// reordered, and each one looks like a bug in the code under test. Serializing the assembly costs a
// few seconds and removes the whole category.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
