using System;

namespace TestFramework.DebugUI.State.Runs;

/// <summary>
/// A place in the code.
/// </summary>
/// <remarks>
/// The file and the line together rather than two fields on whatever holds them: a file with no line is
/// half an answer in an eight-hundred-line test class, and a line with no file is not an answer at all.
/// </remarks>
public sealed record SourceLocation
{
    /// <summary>Gets the file, as it was on the machine that ran the test.</summary>
    public required string FilePath { get; init; }

    /// <summary>Gets the one-based line, or zero when the producer did not report one.</summary>
    public int Line { get; init; }

    /// <summary>Gets the file's own name, for a label with no room for a path.</summary>
    public string FileName => FilePath[(FilePath.LastIndexOfAny(['/', '\\']) + 1)..];

    /// <summary>
    /// Reads a location out of what a run reported, or null when it reported nothing usable.
    /// </summary>
    /// <remarks>
    /// A line without a file is discarded rather than kept as a location pointing nowhere. The reverse is
    /// allowed: a file with no line opens at the top, which is still the right file.
    /// </remarks>
    public static SourceLocation? From(string? filePath, int line)
        => string.IsNullOrWhiteSpace(filePath)
            ? null
            : new SourceLocation { FilePath = filePath, Line = Math.Max(0, line) };
}
