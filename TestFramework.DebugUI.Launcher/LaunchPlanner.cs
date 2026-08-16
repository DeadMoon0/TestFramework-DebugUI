using System;
using System.Collections.Generic;
using System.Linq;

namespace TestFramework.DebugUI.Launcher;

/// <summary>
/// Decides what to start, from what is installed and what the release feed said.
/// </summary>
/// <remarks>
/// <para>
/// Separated from everything that touches the disk or the network so the rules can be tested, because
/// the rules are the part that matters. The one that matters most is that <em>an update must never
/// stand between someone and their tool</em>: a launcher that refuses to start because GitHub is
/// unreachable has turned a convenience into an outage, and it will do so exactly when someone is on
/// a train trying to read a failed run.
/// </para>
/// <para>
/// So the only case that does not start something is the one where there is genuinely nothing to
/// start.
/// </para>
/// </remarks>
public static class LaunchPlanner
{
    /// <summary>Works out what to do.</summary>
    /// <param name="installed">Versions present on this machine.</param>
    /// <param name="latest">What the release feed reported, or <see langword="null"/> if it could not be asked.</param>
    /// <param name="pinned">A version the person explicitly chose, if they chose one.</param>
    public static LaunchDecision Decide(IEnumerable<Version> installed, ReleaseInfo? latest, Version? pinned = null)
    {
        ArgumentNullException.ThrowIfNull(installed);

        List<Version> available = [.. installed.OrderByDescending(version => version)];

        // An explicit choice outranks the feed. Someone who has just rolled back to yesterday's build
        // because today's is broken does not want to be updated forward again on the next start.
        if (pinned is not null && available.Contains(pinned))
        {
            return new LaunchDecision
            {
                Action = LaunchAction.Start,
                Version = pinned,
                Reason = $"Starting {pinned} (chosen)"
            };
        }

        Version? newest = available.FirstOrDefault();

        if (latest is not null && (newest is null || latest.Version > newest))
        {
            return new LaunchDecision
            {
                Action = LaunchAction.Update,
                Release = latest,
                Version = latest.Version,
                Reason = newest is null ? $"Installing {latest.Version}" : $"Updating to {latest.Version}"
            };
        }

        if (newest is not null)
        {
            return new LaunchDecision
            {
                Action = LaunchAction.Start,
                Version = newest,
                Reason = $"Starting {newest}"
            };
        }

        // Nothing installed and nothing offered. This is the only honest failure: there is no
        // application on this machine and no way to get one right now.
        return new LaunchDecision
        {
            Action = LaunchAction.Stuck,
            Reason = "Nothing installed, and the update feed is unreachable"
        };
    }

    /// <summary>
    /// Which versions to delete, keeping the newest few.
    /// </summary>
    /// <remarks>
    /// Old versions are kept deliberately: the reason to roll back is that the newest one broke, and
    /// that is discovered after it is installed. Keeping none would mean the only way back is a
    /// download — over the network that may be the very thing that is unavailable.
    /// </remarks>
    public static IEnumerable<Version> Prunable(IEnumerable<Version> installed, Version running, int keep)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keep);

        return installed
            .OrderByDescending(version => version)
            .Skip(keep)

            // Never the one that is about to run, however old it is: a person who pinned an old
            // version has said that is the one they want.
            .Where(version => version != running)
            .ToList();
    }
}
