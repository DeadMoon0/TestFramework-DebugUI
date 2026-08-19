using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Windows.Input;

namespace TestFramework.DebugUI;

/// <summary>
/// Every keyboard shortcut, defined once.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="RoutedUICommand"/> per action, each carrying its own gesture. That is what makes the
/// settings list and the tooltips readable off the same definition the window actually binds — a
/// shortcut documented in one place and bound in another is a shortcut that eventually lies.
/// </para>
/// <para>
/// Gestures follow the tools people already have open. F5 runs, Shift+F5 stops and Ctrl+, opens
/// settings because that is what Visual Studio and VS Code do; nothing here is novel for its own sake.
/// The exception is F8 to release a breakpoint: F5 is the obvious key and it is already re-run, and
/// re-running when you meant to continue would discard the run you were inspecting.
/// </para>
/// </remarks>
public static class Shortcuts
{
    public static readonly RoutedUICommand Runs =
        Make(nameof(Runs), "Runs", Key.R, ModifierKeys.Control);

    public static readonly RoutedUICommand Settings =
        Make(nameof(Settings), "Settings", Key.OemComma, ModifierKeys.Control);

    public static readonly RoutedUICommand ToggleWatch =
        Make(nameof(ToggleWatch), "Watch on or off", Key.W, ModifierKeys.Control | ModifierKeys.Shift);

    public static readonly RoutedUICommand Rerun =
        Make(nameof(Rerun), "Run the test again", Key.F5, ModifierKeys.None);

    public static readonly RoutedUICommand Stop =
        Make(nameof(Stop), "Stop the run", Key.F5, ModifierKeys.Shift);

    public static readonly RoutedUICommand Continue =
        Make(nameof(Continue), "Release a breakpoint", Key.F8, ModifierKeys.None);

    /// <summary>
    /// Releases the run and stops it again at its next step.
    /// </summary>
    /// <remarks>
    /// F10, which is Step Over everywhere else, so the one key a reader will try first is the one that
    /// works. It is deliberately not next to F8 on the keyboard by accident — these two are pressed
    /// alternately for minutes at a time, and F5 could not be used for either.
    /// </remarks>
    public static readonly RoutedUICommand StepForward =
        Make(nameof(StepForward), "Step to the next step", Key.F10, ModifierKeys.None);

    /// <summary>
    /// Searches the run on screen.
    /// </summary>
    /// <remarks>
    /// Ctrl+F, because that is what it is everywhere and a reader will try it before reading anything. It cost
    /// Fit its gesture: fitting the board is something you do once when a run arrives, and searching it is
    /// something you do all afternoon. Ctrl+Shift+F was not available either — that is the first failure.
    /// </remarks>
    public static readonly RoutedUICommand Search =
        Make(nameof(Search), "Search this run", Key.F, ModifierKeys.Control);

    /// <summary>
    /// Fits the whole run on screen.
    /// </summary>
    /// <remarks>
    /// Ctrl+0, which is reset-the-zoom in every browser and editor — and fitting the board is exactly that.
    /// </remarks>
    public static readonly RoutedUICommand Fit =
        Make(nameof(Fit), "Fit the board", Key.D0, ModifierKeys.Control);

    public static readonly RoutedUICommand Summary =
        Make(nameof(Summary), "Summary", Key.I, ModifierKeys.Control);

    public static readonly RoutedUICommand FirstFailure =
        Make(nameof(FirstFailure), "First failure", Key.F, ModifierKeys.Control | ModifierKeys.Shift);

    public static readonly RoutedUICommand Refresh =
        Make(nameof(Refresh), "Look again on disk", Key.F5, ModifierKeys.Control);

    /// <summary>
    /// Closes whatever is in front.
    /// </summary>
    /// <remarks>
    /// One key for every overlay rather than one per panel. Escape means "get this off my screen", and
    /// which panel that is depends on what is open — a decision the window makes, not the key.
    /// </remarks>
    public static readonly RoutedUICommand CloseTopmost =
        Make(nameof(CloseTopmost), "Close what is in front", Key.Escape, ModifierKeys.None);

    /// <summary>All of them, in the order they are worth reading.</summary>
    public static ImmutableList<RoutedUICommand> All =>
    [
        Runs, Settings, ToggleWatch, CloseTopmost,
        Rerun, Stop, Continue, StepForward, Refresh,
        Search, Fit, Summary, FirstFailure
    ];

    /// <summary>
    /// The command a key press means, or null when it means nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modifiers must match <em>exactly</em>. Three commands share F5 — plain re-runs, Shift stops,
    /// Ctrl looks again on disk — so a comparison that merely checked Ctrl was held would fire the
    /// wrong one, and the wrong one here discards a run.
    /// </para>
    /// <para>
    /// Pure, and separate from the window, so the table can be tested without opening one. It is a
    /// lookup table with three entries on one key: exactly the kind of thing that silently rots.
    /// </para>
    /// </remarks>
    public static RoutedUICommand? Match(Key key, ModifierKeys modifiers)
    {
        foreach (RoutedUICommand command in All)
        {
            foreach (InputGesture candidate in command.InputGestures)
            {
                if (candidate is KeyGesture gesture && gesture.Key == key && gesture.Modifiers == modifiers)
                    return command;
            }
        }

        return null;
    }

    /// <summary>The gesture as a reader would write it, such as <c>Ctrl+Shift+W</c>.</summary>
    public static string GestureOf(RoutedUICommand command)
    {
        if (command?.InputGestures.Count is not > 0 || command.InputGestures[0] is not KeyGesture gesture)
            return string.Empty;

        List<string> parts = [];

        if (gesture.Modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");

        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");

        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");

        parts.Add(Name(gesture.Key));

        return string.Join("+", parts);
    }

    /// <summary>A label with its shortcut, for a tooltip.</summary>
    public static string Describe(string action, RoutedUICommand command)
    {
        string gesture = GestureOf(command);

        return gesture.Length == 0 ? action : $"{action}  ({gesture})";
    }

    private static string Name(Key key) => key switch
    {
        Key.D0 => "0",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Escape => "Esc",
        _ => key.ToString()
    };

    private static RoutedUICommand Make(string name, string text, Key key, ModifierKeys modifiers)
        => new(text, name, typeof(Shortcuts), [new KeyGesture(key, modifiers)]);
}
