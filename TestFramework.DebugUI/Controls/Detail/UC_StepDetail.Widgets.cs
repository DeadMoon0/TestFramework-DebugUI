using System;
using System.Linq;
using System.Reactive.Linq;
using System.Windows;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.Controls.Detail;

// What the step produced to look at, framed as widgets the reader can open on their own.
public partial class UC_StepDetail
{
    /// <summary>
    /// Shows what the step produced to look at, in the order it produced it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of the whole widget channel, seen from a reader's chair: a step that walked four
    /// pages shows four pages rather than the word "Complete". A step that retried shows what each
    /// attempt saw, which is usually where the difference is.
    /// </para>
    /// <para>
    /// Hidden entirely when the step produced none, which is most steps. A section reading "no
    /// widgets" on every step of every run would push the failure and the log further down for
    /// nothing.
    /// </para>
    /// </remarks>
    private void ShowWidgets(RunGraph graph)
    {
        WidgetNode[] widgets = step is null || stageName is null
            ? []
            : [.. graph.Widgets.Where(widget => widget.BelongsTo(stageName, step.StepId))];

        // Rebuilt only when this step's widgets actually changed. The graph is replaced on every
        // event a live run produces, and decoding a row of pictures per log line would make the panel
        // cost more than the run it is showing.
        string signature = string.Join('|', widgets.Select(widget => widget.Description.Body?.ContentHash ?? widget.Name));

        if (string.Equals(signature, shownWidgets, StringComparison.Ordinal))
            return;

        shownWidgets = signature;
        spWidgets.Children.Clear();

        tbWidgetsLabel.Visibility = widgets.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        spWidgets.Visibility = widgets.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (WidgetNode widget in widgets)
            spWidgets.Children.Add(Frame(widget));
    }

    /// <summary>
    /// One widget, framed.
    /// </summary>
    /// <remarks>
    /// Its own control rather than a builder here, because a widget is a thing with a name, a set of
    /// actions and a middle that belongs to whoever produced it — and every surface that shows one
    /// wants all three, not a picture and a caption.
    /// </remarks>
    private UIElement Frame(WidgetNode widget)
    {
        UC_Widget frame = new(shell);

        // The panel says what was asked for and the window does it, the way every other surface here
        // works. Until this was wired the expand button raised an event nobody had subscribed, so
        // pressing it - and clicking the picture, which does the same - did nothing at all.
        frame.Expanded += raised => WidgetOpened?.Invoke(raised);

        frame.Show(widget);

        return frame;
    }
}
