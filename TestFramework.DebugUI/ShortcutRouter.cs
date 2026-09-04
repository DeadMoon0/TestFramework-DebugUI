using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TestFramework.DebugUI;

/// <summary>
/// Turns a key into whichever method the matching button would have called.
/// </summary>
/// <remarks>
/// <para>
/// Matched on a surface's key preview rather than through <c>InputBindings</c> and
/// <c>CommandBindings</c>. That was tried first and silently did nothing: a routed command looks for
/// its binding by walking up from whatever holds keyboard focus, and in this window that is often
/// nothing at all — the board takes mouse focus for panning and the overlays are plain panels — so
/// the walk never reached the window. Previewing the key is not subject to any of that.
/// </para>
/// <para>
/// The gestures come from <see cref="Shortcuts"/>, so the keys matched here are the same ones the
/// settings list and the tooltips display. What each one does is the window's business and arrives
/// through <see cref="Bind"/>; this owns only the matching.
/// </para>
/// </remarks>
internal sealed class ShortcutRouter
{
    private readonly Dictionary<RoutedUICommand, Action> bound = [];

    /// <summary>Watches a surface for the gestures bound to it.</summary>
    /// <param name="surface">Where keys are previewed, which for the tool is the window itself.</param>
    public ShortcutRouter(UIElement surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        surface.PreviewKeyDown += OnKey;
    }

    /// <summary>Points one gesture at what it should run.</summary>
    /// <remarks>
    /// Returns itself so a window can state its whole table as one expression, which is what keeps
    /// the table readable as a table.
    /// </remarks>
    /// <param name="command">The gesture, from <see cref="Shortcuts"/>.</param>
    /// <param name="run">What the matching button calls.</param>
    /// <returns>This router.</returns>
    public ShortcutRouter Bind(RoutedUICommand command, Action run)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(run);

        bound[command] = run;

        return this;
    }

    /// <summary>
    /// Runs whichever shortcut the key matches.
    /// </summary>
    /// <remarks>
    /// Typing is left alone: a gesture with no modifier — Escape, F5, F8 — would otherwise fire while
    /// someone was typing into a field. There is no text entry in this window today, and this is what
    /// stops the first one that appears from being broken by these shortcuts.
    /// </remarks>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBoxBase { IsReadOnly: false })
            return;

        if (Shortcuts.Match(e.Key, Keyboard.Modifiers) is not { } command)
            return;

        if (!bound.TryGetValue(command, out Action? run))
            return;

        run();
        e.Handled = true;
    }
}
