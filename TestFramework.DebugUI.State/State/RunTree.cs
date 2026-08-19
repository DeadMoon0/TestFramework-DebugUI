using System;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.State;

/// <summary>
/// The runs a window knows about, arranged the way a person looks for one: project, then class, then
/// test, then the executions of it.
/// </summary>
/// <remarks>
/// <para>
/// A flat list of runs answers "what happened recently". It does not answer "how is this test
/// doing", which is the question anyone re-running a failure actually has — five runs of one test
/// read as five unrelated rows. The grouping is the answer, and it is computed here rather than in
/// the view so it can be tested and so two surfaces cannot group the same runs differently.
/// </para>
/// <para>
/// The class is a level of its own rather than part of each test's label. Written into the label it
/// repeated on every row of a suite — twenty rows reading <c>RunValueOutputTests.Something</c>, each
/// truncated in the middle of the part that differed — which spent the rail's whole width on the one
/// piece of the name the rows had in common.
/// </para>
/// </remarks>
public static class RunTree
{
    /// <summary>Groups runs by project, class and test, newest run first within each test.</summary>
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
                    Classes =
                    [
                        .. project
                            .GroupBy(run => ClassOf(run.Test), StringComparer.Ordinal)
                            .Select(type => new ClassGroup
                            {
                                Class = type.Key,
                                DisplayName = Shorten(type.Key),
                                Tests =
                                [
                                    .. type
                                        .GroupBy(run => run.Test, StringComparer.Ordinal)
                                        .Select(test => new TestGroup
                                        {
                                            Test = test.Key,
                                            DisplayName = Leaf(test.Key),
                                            Runs = [.. test.OrderByDescending(run => run.StartedAtUtc)]
                                        })

                                        // Tests ordered by their most recent run, so what someone is
                                        // working on right now stays at the top rather than sinking
                                        // under an alphabet.
                                        .OrderByDescending(test => test.Runs.Max(run => run.StartedAtUtc))
                                ]
                            })
                            .OrderByDescending(type => type.Tests.Max(test => test.Runs.Max(run => run.StartedAtUtc)))
                    ]
                })
                .OrderByDescending(project => project.Runs.Max(run => run.StartedAtUtc))
        ];
    }

    /// <summary>
    /// The class a test belongs to, which is its name up to the last dot.
    /// </summary>
    /// <remarks>
    /// A run with no test identity has no class either. It groups under a name of its own rather than
    /// being dropped or filed under an empty heading — the tool is often pointed at a host process that
    /// never reported which test it was running, and those runs are still worth opening.
    /// </remarks>
    public static string ClassOf(string test)
    {
        if (string.IsNullOrWhiteSpace(test))
            return NoIdentity;

        int lastDot = test.LastIndexOf('.');

        return lastDot > 0 ? test[..lastDot] : NoIdentity;
    }

    /// <summary>The name given to runs that never reported which test they were.</summary>
    public const string NoIdentity = "No test identity";

    /// <summary>
    /// Whether a run answers a name typed into the tree's filter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched against every name the tree files a run under — its project, its class, its test and its own
    /// display name — so naming any level narrows to that branch. Someone typing a class name and someone
    /// typing a test name are both asking the same question of the same box.
    /// </para>
    /// <para>
    /// This filters the runs, not the tree. The grouping is derived from whatever survives, so a project
    /// keeps appearing exactly as long as one of its runs matches and there is no separate pruning pass to
    /// get wrong.
    /// </para>
    /// </remarks>
    public static bool Matches(RunSummary run, SearchPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(pattern);

        return pattern.MatchesAny(run.Project, ClassOf(run.Test), run.Test, run.Name);
    }

    /// <summary>
    /// Whether a run is one of the few worth putting in front of the reader.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything except "passed" and "never opened". Those two are the bulk of any journal — a suite
    /// that is behaving produces fifty passes, and a tool opened after the fact has never replayed any
    /// of them — and giving each of those a card is what turns a page into a wall.
    /// </para>
    /// <para>
    /// Unproven counts. A run that finished without asserting anything did not fail, but it did not
    /// prove anything either, and it is exactly the case a reader would otherwise never think to look
    /// for.
    /// </para>
    /// </remarks>
    public static bool NeedsAttention(RunHealth health)
        => health is RunHealth.Waiting or RunHealth.Running or RunHealth.Failed or RunHealth.Aborted or RunHealth.Unproven;

    /// <summary>
    /// The part of a qualified class name worth showing.
    /// </summary>
    /// <remarks>
    /// Every class in a suite shares its leading namespaces, so a name trimmed from the right shows
    /// the same prefix on every row. The tail is what tells them apart.
    /// </remarks>
    private static string Shorten(string type) => Leaf(type);

    private static string Leaf(string name)
    {
        int lastDot = name.LastIndexOf('.');

        return lastDot >= 0 && lastDot < name.Length - 1 ? name[(lastDot + 1)..] : name;
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

/// <summary>One project, and the classes run from it.</summary>
public sealed record ProjectGroup
{
    /// <summary>Gets the project name.</summary>
    public required string Project { get; init; }

    /// <summary>Gets the test classes, most recently run first.</summary>
    public required ImmutableList<ClassGroup> Classes { get; init; }

    /// <summary>
    /// Gets the tests, most recently run first, without their class grouping.
    /// </summary>
    /// <remarks>
    /// For callers that want the tests and do not care which class they came from. Flattened here rather
    /// than at each call site so "most recently run first" means the same thing everywhere.
    /// </remarks>
    public ImmutableList<TestGroup> Tests =>
    [
        .. Classes
            .SelectMany(type => type.Tests)
            .OrderByDescending(test => test.Runs.Max(run => run.StartedAtUtc))
    ];

    /// <summary>Gets every run in the project.</summary>
    public ImmutableList<RunSummary> Runs => [.. Classes.SelectMany(type => type.Runs)];

    /// <summary>Gets how the project stands, taken from its worst run.</summary>
    public RunHealth Health => RunTree.WorstOf(Runs);
}

/// <summary>One test class, and the tests in it.</summary>
public sealed record ClassGroup
{
    /// <summary>Gets the class's identity, which is the qualified test name up to its last dot.</summary>
    public required string Class { get; init; }

    /// <summary>Gets the part of the name worth showing.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the tests, most recently run first.</summary>
    public required ImmutableList<TestGroup> Tests { get; init; }

    /// <summary>Gets every run of every test in the class.</summary>
    public ImmutableList<RunSummary> Runs => [.. Tests.SelectMany(test => test.Runs)];

    /// <summary>Gets how the class stands, taken from its worst run.</summary>
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
