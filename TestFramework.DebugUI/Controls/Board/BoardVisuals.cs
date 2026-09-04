using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace TestFramework.DebugUI.Controls.Board;

/// <summary>
/// What is on the board, kept so it can be repainted without being rebuilt.
/// </summary>
/// <remarks>
/// The seam between composing a board and painting one. A step going from running to complete moves
/// nothing, so it repaints rather than relaying out - and repainting means reaching the same elements
/// again, by the key the layout gave them.
/// </remarks>
internal sealed class BoardVisuals
{
    /// <summary>Nothing drawn yet.</summary>
    public static BoardVisuals Empty { get; } = new();

    /// <summary>Every step's card, by the layout's key for it.</summary>
    public Dictionary<string, StepVisual> Steps { get; } = new(StringComparer.Ordinal);

    /// <summary>The coloured stroke of each pipe, kept so it can dim or light as its value appears.</summary>
    public Dictionary<string, Path> Pipes { get; } = new(StringComparer.Ordinal);

    /// <summary>The verdict box, when the run has one.</summary>
    public VerdictVisual? Verdict { get; set; }
}

internal sealed record StepVisual(
    Border Box,
    Border Status,
    TextBlock Name,
    TextBlock Note,
    TextBlock Outputs,
    TextBlock Elapsed,
    Border Breakpoint,
    Border Widget);

internal sealed record VerdictVisual(Border Box, TextBlock Heading, TextBlock Why);
