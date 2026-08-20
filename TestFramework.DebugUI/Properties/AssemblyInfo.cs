using System.Runtime.CompilerServices;

// Named for the test assembly that actually exists. The previous grant named "TestFramework.DebugUI.Tests",
// which no project builds — so it granted nothing, and every internal in this assembly was untestable without
// anybody noticing.
[assembly: InternalsVisibleTo("TestFramework.DebugUI.App.Tests")]