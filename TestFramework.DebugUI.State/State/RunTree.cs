using System;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.State;

/// <summary>
/// The runs a window knows about, arranged the way a person looks for one: project, then test, then
/// the executions of it.
/// </summary>
/// <remarks>
/// A flat list of runs answers "what happened recently". It does not answer "how is this test
/// doing", which is the question anyone re-running a failure actually has — five runs of one test
/// read as five unrelated rows. The grouping is the answer, and it is computed here rather than in
/// the view so it can be tested and so two surfaces cannot group the same runs differently.
/// </remarks>
public static class RunTree
{
    /// <summary>Groups runs by project and test, newest run first within each test.</summary>
    public static ImmutableList<ProjectGroup> Of(ImmutableList<RunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        return
        [
            .. runs
                .GroupBy(run => run.Project, StringComparer.Ordinal)
                .Select(project => new ProjectGroup
                {
                    Project = project.Key,
                    Tests =
                    [
                        .. project
                            .GroupBy(run => run.Test, StringComparer.Ordinal)
                            .Select(test => new TestGroup
                            {
                                Test = test.Key,
                                DisplayName = Shorten(test.Key),
                                Runs = [.. test.OrderByDescending(run => run.StartedAtUtc)]
                            })

                            // Tests ordered by their most recent run, so what someone is working on
                            // right now stays at the top rather than sinking under an alphabet.
                            .OrderByDescending(test => test.Runs.Max(run => run.StartedAtUtc))
                    ]
                })
                .OrderByDescending(project => project.Tests.Max(test => test.Runs.Max(run => run.StartedAtUtc)))
        ];
    }

    /// <summary>
    /// The part of a qualified test name worth showing.
    /// </summary>
    /// <remarks>
    /// Every test in a suite shares its leading namespaces, so a name trimmed from the right shows
    /// the same prefix on every row. The tail is what tells them apart.
    /// </remarks>
    private static string Shorten(string test)
    {
        string[] parts = test.Split('.', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length <= 2 ? test : string.Join('.', parts[^2..]);
    }

    /// <summary>
    /// The worst thing that happened in a set of runs.
    /// </summary>
    /// <remarks>
    /// Rolled up by severity rather than by recency: a group whose latest run passed but which
    /// contains a failure is not a passing group, and a header that said so would hide the one run
    /// worth opening. Live states win outright — something still happening is the most useful thing
    /// a header can say.
    /// </remarks>
    public static RunHealth WorstOf(ImmutableList<RunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        if (runs.Count == 0)
            return RunHealth.Unknown;

        RunHealth[] order =
        [
            RunHealth.Waiting,
            RunHealth.Running,
            RunHealth.Failed,
            RunHealth.Aborted,
            RunHealth.Unproven,
            RunHealth.Passed,
            RunHealth.Unknown
        ];

        foreach (RunHealth health in order)
        {
            if (runs.Any(run => run.Health == health))
                return health;
        }

        return RunHealth.Unknown;
    }
}

/// <summary>One project, and the tests run from it.</summary>
public sealed record ProjectGroup
{
    /// <summary>Gets the project name.</summary>
    public required string Project { get; init; }

    /// <summary>Gets the tests, most recently run first.</summary>
    public required ImmutableList<TestGroup> Tests { get; init; }

    /// <summary>Gets every run in the project.</summary>
    public ImmutableList<RunSummary> Runs => [.. Tests.SelectMany(test => test.Runs)];

    /// <summary>Gets how the project stands, taken from its worst run.</summary>
    public RunHealth Health => RunTree.WorstOf(Runs);
}

/// <summary>One test, and the runs of it.</summary>
public sealed record TestGroup
{
    /// <summary>Gets the test's identity, which is its fully qualified name where there is one.</summary>
    public required string Test { get; init; }

    /// <summary>Gets the part of the name worth showing.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the runs, newest first.</summary>
    public required ImmutableList<RunSummary> Runs { get; init; }

    /// <summary>Gets how the test stands, taken from its worst run.</summary>
    public RunHealth Health => RunTree.WorstOf(Runs);
}
