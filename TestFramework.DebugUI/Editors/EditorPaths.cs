using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using TestFramework.DebugUI.State.Runs;

namespace TestFramework.DebugUI.Editors;

/// <summary>
/// Where the editors live, and what to hand them.
/// </summary>
/// <remarks>
/// Every method here takes its view of the disk as a delegate rather than touching it. Finding an
/// installation is exactly the kind of logic that cannot be tested on the machine that has one — the
/// interesting cases are the machine with neither editor, the machine with a portable install, and
/// vswhere returning something that is not a path at all.
/// </remarks>
public static class EditorPaths
{
    /// <summary>Where vswhere is installed, relative to the 32-bit program files directory.</summary>
    /// <remarks>
    /// Fixed by Visual Studio's installer and documented as such, which is the only reason it is safe to
    /// hard-code a path to a Microsoft tool: it is the one file guaranteed to be where it is.
    /// </remarks>
    public const string VsWhereRelativePath = @"Microsoft Visual Studio\Installer\vswhere.exe";

    /// <summary>The arguments that make vswhere print the newest installation's executable.</summary>
    /// <remarks>
    /// <c>-prerelease</c> is included so a machine with only a preview installed is not reported as having
    /// no Visual Studio at all.
    /// </remarks>
    public static readonly string VsWhereArguments = "-latest -prerelease -property productPath";

    /// <summary>
    /// Everywhere VS Code might be, most likely first.
    /// </summary>
    /// <remarks>
    /// The per-user directory leads because that is what VS Code's own installer defaults to, so it is
    /// where most machines have it. A missing environment variable yields no candidate rather than a path
    /// rooted at nothing.
    /// </remarks>
    public static ImmutableList<string> CodeCandidates(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        string[] roots =
        [
            environment("LOCALAPPDATA") is { Length: > 0 } local ? Path.Combine(local, "Programs") : string.Empty,
            environment("ProgramFiles") ?? string.Empty,
            environment("ProgramFiles(x86)") ?? string.Empty
        ];

        return
        [
            .. roots
                .Where(root => root.Length > 0)
                .Select(root => Path.Combine(root, "Microsoft VS Code", "Code.exe"))
        ];
    }

    /// <summary>The first candidate that is actually there, or null when none of them is.</summary>
    public static string? FirstExisting(IEnumerable<string> candidates, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(exists);

        return candidates.FirstOrDefault(candidate => candidate.Length > 0 && exists(candidate));
    }

    /// <summary>
    /// The executable vswhere reported, or null when it reported anything else.
    /// </summary>
    /// <remarks>
    /// vswhere prints one path per line and prints nothing at all when no installation matches. It can
    /// also fail and write a message, so the result is only accepted if it looks like an executable — a
    /// diagnostic passed off as a path would become a launch that fails in front of the user.
    /// </remarks>
    public static string? ProductPathFrom(string? vswhereOutput)
    {
        if (string.IsNullOrWhiteSpace(vswhereOutput))
            return null;

        foreach (string line in vswhereOutput.Split('\n'))
        {
            string candidate = line.Trim();

            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// The solution a project belongs to, looked for beside it and then above it.
    /// </summary>
    /// <remarks>
    /// Opening the solution rather than the project is what makes the button useful: a project opened on
    /// its own in Visual Studio is missing every other project the test touches. Bounded, because walking
    /// to the root of the drive would eventually find somebody else's solution.
    /// </remarks>
    public static string? SolutionNear(string projectFilePath, Func<string, IEnumerable<string>> solutionsIn, int levels = 4)
    {
        ArgumentNullException.ThrowIfNull(solutionsIn);

        if (string.IsNullOrWhiteSpace(projectFilePath))
            return null;

        string? directory = Path.GetDirectoryName(projectFilePath);

        for (int level = 0; level <= levels && !string.IsNullOrEmpty(directory); level++)
        {
            string? solution = solutionsIn(directory)
                .Where(found => !string.IsNullOrWhiteSpace(found))
                .OrderBy(found => found, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (solution is not null)
                return solution;

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    /// <summary>
    /// What to open for a run.
    /// </summary>
    /// <param name="projectFilePath">The project file the run reported, when it reported one.</param>
    /// <param name="wantsFolder">
    /// Whether the editor should be given the containing folder instead of the file. VS Code works on a
    /// folder — handed a solution file it opens one file and shows none of the code around it — whereas
    /// Visual Studio wants the solution itself.
    /// </param>
    /// <param name="solutionsIn">The solution files in a directory.</param>
    public static string? TargetFor(string? projectFilePath, bool wantsFolder, Func<string, IEnumerable<string>> solutionsIn)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return null;

        string best = SolutionNear(projectFilePath, solutionsIn) ?? projectFilePath;

        return wantsFolder ? Path.GetDirectoryName(best) : best;
    }

    /// <summary>
    /// The whole command line for opening a run, landing on the line that started it when the run said
    /// which one that was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two editors are asked for this in genuinely different ways, which is why this returns arguments
    /// rather than a path.
    /// </para>
    /// <para>
    /// VS Code takes the folder <em>and</em> the position, so it loses nothing: the workspace opens and the
    /// caret lands on the line. Visual Studio has no way to say a line on its command line at all, so it is
    /// given <c>/edit</c> and the file — which opens in the instance the reader already has that solution
    /// open in, this being someone looking at a test they just ran. With no instance running it opens the
    /// file on its own, which is worse than the solution and is the price of landing on the right file; a
    /// run that reported no source file still gets the solution, exactly as before.
    /// </para>
    /// </remarks>
    public static ImmutableList<string> ArgumentsFor(
        string? projectFilePath,
        bool wantsFolder,
        SourceLocation? source,
        Func<string, IEnumerable<string>> solutionsIn)
    {
        string? target = TargetFor(projectFilePath, wantsFolder, solutionsIn);

        if (source is null)
            return target is null ? [] : [target];

        if (!wantsFolder)
            return ["/edit", source.FilePath];

        string position = source.Line > 0
            ? source.FilePath + ":" + source.Line.ToString(CultureInfo.InvariantCulture)
            : source.FilePath;

        return target is null ? ["--goto", position] : [target, "--goto", position];
    }
}
