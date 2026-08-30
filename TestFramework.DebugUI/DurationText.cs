using System;
using System.Globalization;

namespace TestFramework.DebugUI;

/// <summary>
/// How long something took, written the one way this tool writes it.
/// </summary>
/// <remarks>
/// <para>
/// There were five of these, three of them identical and two that had drifted, so the same step could
/// read as <c>1.4 s</c> on the board and <c>1.4 s</c> in one panel and <c>1m 24s</c>, <c>1m 5s</c> or
/// <c>1.4 min</c> in the others — a reader comparing two panels was comparing two formats. A number
/// that appears in several places is one rule, and it lives in one place.
/// </para>
/// <para>
/// The bands are chosen for reading rather than for precision: a duration is skimmed next to other
/// durations, so each band shows about three significant figures and no more, and the unit is always
/// written out because a bare number beside another bare number invites the wrong comparison.
/// </para>
/// </remarks>
public static class DurationText
{
    /// <summary>
    /// One duration, as short as it can be while still saying what it is.
    /// </summary>
    /// <remarks>
    /// A step under a millisecond says so rather than rounding to <c>0 ms</c>, which reads as a step
    /// that never ran. That is the true statement about it: too fast to have a number worth comparing.
    /// </remarks>
    public static string Compact(TimeSpan duration)
    {
        // A negative span reaches here from a comparison against an earlier run — "quicker by" is the
        // caller's word for it, and the sign belongs in that sentence rather than in the number.
        TimeSpan value = duration < TimeSpan.Zero ? duration.Duration() : duration;

        return value.TotalMilliseconds switch
        {
            < 1 => "<1 ms",
            < 1_000 => value.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture) + " ms",
            < 60_000 => value.TotalSeconds.ToString("0.#", CultureInfo.CurrentCulture) + " s",
            < 3_600_000 => $"{(int)value.TotalMinutes}m {value.Seconds:00}s",
            _ => $"{(int)value.TotalHours}h {value.Minutes:00}m"
        };
    }
}
