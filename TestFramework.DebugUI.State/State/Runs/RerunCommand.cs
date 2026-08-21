using System;
using System.IO;

namespace TestFramework.DebugUI.State.Runs;

/// <summary>
/// The command that runs one test again, or the reason there is not one.
/// </summary>
/// <remarks>
/// <para>
/// A run knows which test produced it, so the UI can offer to run that test again rather than
/// leaving someone to find it in an IDE, remember its name, and start it by hand — which is the
/// loop this tool exists to shorten.
/// </para>
/// <para>
/// Worked out apart from anything that starts a process, because the interesting part is what makes
/// a re-run <em>impossible</em>. An identity that is missing a piece must produce a stated reason,
/// never a command built on a guess: a filter guessed wrong does not fail, it runs the wrong tests
/// and reports success.
/// </para>
/// </remarks>
public sealed record RerunCommand
{
    /// <summary>Gets the program to start.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets its arguments.</summary>
    public required string Arguments { get; init; }

    /// <summary>Gets the directory to start it in.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Gets the command as a person would type it, for the feed and for a tooltip.</summary>
    public string Display => $"{FileName} {Arguments}";

    /// <summary>
    /// Builds the command for a run, or says why it cannot be built.
    /// </summary>
    /// <param name="run">The run to repeat.</param>
    /// <param name="reason">Why no command could be built, when none could.</param>
    public static bool TryFor(RunSummary run, out RerunCommand? command, out string reason)
    {
        ArgumentNullException.ThrowIfNull(run);

        command = null;

        // The producer's own verdict first. It knows whether the identity it resolved is complete,
        // and second-guessing it here would be two rules for one question.
        if (!run.CanRerun)
        {
            reason = "This run did not carry enough identity to be repeated.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(run.FullyQualifiedName))
        {
            reason = "This run did not report which test it was.";
            return false;
        }

        // A project file, not the assembly: under a test runner the assembly path is the host
        // process, and `dotnet test` needs something it can build. Anything else is a guess.
        if (string.IsNullOrWhiteSpace(run.ProjectFilePath) || !run.ProjectFilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            reason = "This run did not report a project file to run the test from.";
            return false;
        }

        command = new RerunCommand
        {
            FileName = "dotnet",

            // Quoted, because a project path with a space in it is ordinary and an unquoted one
            // silently becomes two arguments and a confusing error.
            Arguments = $"test \"{run.ProjectFilePath}\" --filter \"FullyQualifiedName={run.FullyQualifiedName}\"",
            WorkingDirectory = Path.GetDirectoryName(run.ProjectFilePath) ?? string.Empty
        };

        reason = string.Empty;
        return true;
    }

    /// <summary>Whether a run could be repeated, without building the command.</summary>
    public static bool IsAvailableFor(RunSummary? run) => run is not null && TryFor(run, out _, out _);
}
