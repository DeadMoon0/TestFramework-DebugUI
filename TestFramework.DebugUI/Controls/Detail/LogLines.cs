using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// How a step's log reads.
/// </summary>
/// <remarks>
/// <para>
/// One entry is a time, a severity and a message, and it is those three things whether it is being drawn as
/// a row or written to the clipboard. Keeping the wording here means the log someone pastes into a ticket
/// says what the panel said.
/// </para>
/// <para>
/// Local time, like every other time in this window. The record on disk is UTC and stays that way; a reader
/// comparing the log against their own clock is the only reason they are looking at the timestamp.
/// </para>
/// </remarks>
public static class LogLines
{
    /// <summary>The severity, short enough to sit in a column beside the time.</summary>
    /// <remarks>
    /// Fixed width across the three, so the messages start at the same place on every row. Spelled-out
    /// levels were what made the old log unreadable: "Information" is eleven characters of the width the
    /// message needed, repeated down every line.
    /// </remarks>
    public static string Tag(DebugLogLevel level) => level switch
    {
        DebugLogLevel.Warning => "WARN",
        DebugLogLevel.Error => "FAIL",
        _ => "INFO"
    };

    /// <summary>Which brush the severity is drawn in.</summary>
    /// <remarks>
    /// The lifecycle colours, so a warning in the log is the same amber as an unproven step and a failure the
    /// same red as a broken one. Information is faint: it is the ordinary case, and colouring it would leave a
    /// log of forty ordinary lines looking like forty things to look at.
    /// </remarks>
    public static string Brush(DebugLogLevel level) => level switch
    {
        DebugLogLevel.Warning => "StateTimeout",
        DebugLogLevel.Error => "StateError",
        _ => "TextFaint"
    };

    /// <summary>When an entry was emitted, on the reader's clock.</summary>
    public static string Time(LogNode entry)
        => (entry ?? throw new ArgumentNullException(nameof(entry)))
            .OccurredAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>One entry as a line of text.</summary>
    public static string Of(LogNode entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return $"{Time(entry)}  {Tag(entry.Level)}  {entry.Render()}";
    }

    /// <summary>A whole log as text, ready for the clipboard.</summary>
    public static string Text(IEnumerable<LogNode> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return string.Join(Environment.NewLine, entries.Select(Of));
    }

    /// <summary>How many entries there are, in words, for the heading.</summary>
    public static string Count(int entries) => entries == 1 ? "1 entry" : $"{entries} entries";
}
