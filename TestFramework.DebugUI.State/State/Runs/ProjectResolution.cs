using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace TestFramework.DebugUI.State.Runs;

/// <summary>
/// Finds, on this machine, the project a run came from somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// A shared run carries a complete identity — the qualified test name, the project's file name, the source file
/// — but every path in it is the sender's. Re-running it and opening it in an editor both need one thing: a
/// project file that exists <em>here</em>. Nothing more has to travel; what is missing is the translation.
/// </para>
/// <para>
/// Translated in three steps, from the cheapest to the least certain. The recorded path is tried first, because
/// on the machine that produced the run it is simply right. Then the reader's own history: any run they have
/// made themselves says where a project of that name lives here, which costs no search and no configuration.
/// Then, from any pair that did match, the difference between the two roots is learned and applied to the runs
/// that are left — so one project the reader happens to share resolves every other project in the bundle.
/// </para>
/// <para>
/// Nothing here guesses. A name that matches nothing stays unresolved and re-run stays unavailable with its
/// reason, because a re-run aimed at the wrong project does not fail — it runs different tests and reports
/// success.
/// </para>
/// </remarks>
public static class ProjectResolution
{
    /// <summary>
    /// The difference between where a path was and where the same thing lives here.
    /// </summary>
    /// <remarks>
    /// Learned from one project that resolved, then applied to the rest. The two paths agree from some point
    /// rightwards — the repository's own shape — and disagree to the left of it, which is the part that belongs
    /// to a machine rather than to a repository.
    /// </remarks>
    public sealed record RootMap
    {
        /// <summary>Gets the prefix as the sender had it.</summary>
        public required string From { get; init; }

        /// <summary>Gets the prefix it becomes here.</summary>
        public required string To { get; init; }

        /// <summary>
        /// Learns the mapping between two paths for the same thing, or nothing if they share no tail.
        /// </summary>
        /// <remarks>
        /// Compared by path segment rather than by character, so a root ending mid-name cannot be invented: two
        /// paths ending <c>…/Alpha.Tests/Alpha.Tests.csproj</c> share two segments, not the nineteen characters
        /// that happen to line up.
        /// </remarks>
        public static RootMap? Learn(string? senderPath, string? localPath)
        {
            if (string.IsNullOrWhiteSpace(senderPath) || string.IsNullOrWhiteSpace(localPath))
                return null;

            string[] sender = Segments(senderPath);
            string[] local = Segments(localPath);

            int shared = 0;

            while (shared < sender.Length
                   && shared < local.Length
                   && string.Equals(sender[^(shared + 1)], local[^(shared + 1)], StringComparison.OrdinalIgnoreCase))
            {
                shared++;
            }

            // A single shared segment is just two files with the same name; it says nothing about their roots.
            if (shared < 2)
                return null;

            return new RootMap
            {
                From = string.Join(Path.DirectorySeparatorChar, sender[..^shared]),
                To = string.Join(Path.DirectorySeparatorChar, local[..^shared])
            };
        }

        /// <summary>Rewrites a path from the sender's world into this one, or nothing if it does not apply.</summary>
        public string? Apply(string? senderPath)
        {
            if (string.IsNullOrWhiteSpace(senderPath) || From.Length == 0)
                return null;

            string normalised = Normalise(senderPath);

            if (!normalised.StartsWith(From, StringComparison.OrdinalIgnoreCase))
                return null;

            return To + normalised[From.Length..];
        }
    }

    /// <summary>
    /// The local project file for every run whose recorded one is not on this machine.
    /// </summary>
    /// <remarks>
    /// Only the runs that need translating are returned, so a caller can treat the result as a set of overrides
    /// and leave everything else exactly as the producer recorded it.
    /// </remarks>
    /// <param name="runs">Every run the picker knows about, local and imported alike.</param>
    /// <param name="exists">Whether a path is on this machine. Injected so the rules can be tested.</param>
    public static ImmutableDictionary<string, string> ResolveAll(
        IEnumerable<RunSummary> runs,
        Func<string, bool>? exists = null)
    {
        ArgumentNullException.ThrowIfNull(runs);

        Func<string, bool> onDisk = exists ?? File.Exists;
        List<RunSummary> all = [.. runs];

        // Every project this machine can actually build, taken from the reader's own runs. This is the index
        // that makes the whole thing free: no crawl, no configuration, and it is already in memory.
        List<string> local =
        [
            .. all
                .Select(run => run.ProjectFilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && onDisk(path!))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        Dictionary<string, string> resolved = [];
        List<RootMap> maps = [];
        List<RunSummary> unresolved = [];

        foreach (RunSummary run in all)
        {
            string? recorded = run.ProjectFilePath;

            if (string.IsNullOrWhiteSpace(recorded) || onDisk(recorded))
                continue;

            if (ByName(recorded, local) is { } match)
            {
                resolved[run.SessionId] = match;

                if (RootMap.Learn(recorded, match) is { } learned)
                    maps.Add(learned);

                continue;
            }

            unresolved.Add(run);
        }

        // The second pass. A project nobody here has ever run cannot be found by name, but if any of its
        // neighbours resolved then the two roots are now known and the rest follow from them.
        foreach (RunSummary run in unresolved)
        {
            foreach (RootMap map in maps)
            {
                if (map.Apply(run.ProjectFilePath) is { } candidate && onDisk(candidate))
                {
                    resolved[run.SessionId] = candidate;
                    break;
                }
            }
        }

        return resolved.ToImmutableDictionary(StringComparer.Ordinal);
    }

    /// <summary>A local project with the same file name, when there is exactly one thing to go on.</summary>
    private static string? ByName(string recorded, IEnumerable<string> local)
    {
        string name = Path.GetFileName(recorded);

        if (string.IsNullOrWhiteSpace(name))
            return null;

        return local.FirstOrDefault(candidate =>
            string.Equals(Path.GetFileName(candidate), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] Segments(string path)
        => Normalise(path).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// One separator, so a path written on another machine compares against one written here.
    /// </summary>
    /// <remarks>
    /// A journal can carry either separator: the framework records what the host gave it, and an anonymous
    /// export leaves a placeholder in front of the rest. Neither is worth a special case further in.
    /// </remarks>
    private static string Normalise(string path)
        => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}
