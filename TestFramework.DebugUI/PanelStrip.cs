using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using TestFramework.DebugUI.Controls.Dock;
using TestFramework.DebugUI.Docking;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI;

/// <summary>
/// The row of panel icons in the title bar.
/// </summary>
/// <remarks>
/// Its own class rather than a stretch of the window, because it answers only to the arrangement: it
/// is built from the registry, rebuilt when panels are grouped or moved, and lit by what is open. The
/// window neither knows nor decides any of that - it owns the strip's place on screen and nothing else.
/// </remarks>
internal sealed class PanelStrip
{
    private readonly Panel host;

    /// <summary>Draws the strip into a container of the title bar's.</summary>
    /// <param name="host">Where the icons go. Resources are found through it, so it must be in the tree.</param>
    public PanelStrip(Panel host)
    {
        this.host = host;
    }

    /// <summary>
    /// Fills the title bar's panel strip from the registry, grouped by card.
    /// </summary>
    /// <remarks>
    /// Built rather than written out in markup, so a panel that exists always has a way to reach it — the same
    /// reason the settings page lists shortcuts off the command table instead of a copy of it.
    ///
    /// Rebuilt whenever the arrangement changes, because the grouping is a fact about the arrangement: two panels
    /// sharing a card are one thing on screen, and their icons have to sit together and act together to say so.
    /// </remarks>
    public void Rebuild()
    {
        host.Children.Clear();

        DockLayout layout = Arrangement.Current;

        foreach (ImmutableList<PanelId> card in DockGrouping.Cards(layout))
        {
            bool shared = card.Count > 1;

            StackPanel row = new() { Orientation = Orientation.Horizontal };

            for (int index = 0; index < card.Count; index++)
            {
                // A hairline between the parts, which is what turns two icons in a box into one control with two
                // halves. Without it a bordered pair reads as two buttons that happen to be boxed in together.
                if (index > 0)
                {
                    row.Children.Add(new Border
                    {
                        Width = 1,
                        Margin = new Thickness(0, 7, 0, 7),
                        Background = (Brush)host.FindResource(ThemeKeys.IconGroupEdge)
                    });
                }

                row.Children.Add(PanelButton(card[index], card));
            }

            // Drawn as a segmented control: an outline round the pair, a divider between them, and no gap either
            // side of the divider. A plate alone was not enough — it read as spacing rather than as meaning, and
            // closing one icon while the other went with it came as a surprise. The shape now says they are one
            // thing before it is clicked, and the tooltip says it in words.
            Border group = new()
            {
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(shared ? 5 : 0, 0, shared ? 5 : 0, 0),
                Padding = new Thickness(shared ? 2 : 0, 0, shared ? 2 : 0, 0),
                Background = shared ? (Brush)host.FindResource(ThemeKeys.SurfaceRaised) : Brushes.Transparent,
                BorderBrush = shared ? (Brush)host.FindResource(ThemeKeys.IconGroupEdge) : Brushes.Transparent,
                BorderThickness = new Thickness(shared ? 1 : 0),
                Child = row
            };

            WindowChrome.SetIsHitTestVisibleInChrome(group, true);

            host.Children.Add(group);
        }

        ShowState();
    }

    /// <summary>One panel's icon in the strip.</summary>
    /// <param name="panel">The panel the icon opens and closes.</param>
    /// <param name="card">
    /// Every panel drawn in the same card, so the tooltip can name what else goes away with this one. Being told
    /// afterwards is what made the grouping feel like a bug rather than a rule.
    /// </param>
    private Button PanelButton(PanelId panel, ImmutableList<PanelId> card)
    {
        PanelDescriptor descriptor = PanelRegistry.Of(panel);

        Path glyph = new()
        {
            Data = (Geometry)host.FindResource(descriptor.IconKey),
            Stroke = (Brush)host.FindResource(ThemeKeys.TextSecondary),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 18,
            Height = 18,
            Stretch = Stretch.None
        };

        Button button = new()
        {
            Style = (Style)host.FindResource(ThemeKeys.CaptionIconButton),
            Content = glyph,
            Tag = panel,

            ToolTip = Tip(panel, descriptor, card)
        };

        WindowChrome.SetIsHitTestVisibleInChrome(button, true);

        button.Click += (_, _) => TogglePanel(panel);

        return button;
    }

    /// <summary>
    /// What an icon says it does.
    /// </summary>
    /// <remarks>
    /// Named in the casing a reader says it in rather than the header's shouted form, and the runs page carries
    /// its shortcut because it is the one panel with a key of its own. A panel sharing a card names its company:
    /// the outline says two icons act together, and this says which panel the other one is.
    /// </remarks>
    private static string Tip(PanelId panel, PanelDescriptor descriptor, ImmutableList<PanelId> card)
    {
        string name = panel == PanelId.Home
            ? Shortcuts.Describe("Every run, with what became of each", Shortcuts.Runs)
            : Spoken(descriptor.Title);

        ImmutableList<string> company =
        [
            .. card
                .Where(member => member != panel)
                .Select(member => Spoken(PanelRegistry.Of(member).Title))
        ];

        return company.Count == 0 ? name : $"{name}  ·  shares a card with {string.Join(", ", company)}, and closes with it";
    }

    /// <summary>A panel's name as a reader would say it, rather than as its header shouts it.</summary>
    private static string Spoken(string title)
        => char.ToUpperInvariant(title[0]) + title[1..].ToLowerInvariant();

    /// <summary>
    /// Puts a panel away, or brings it back.
    /// </summary>
    /// <remarks>
    /// Closing takes the whole card with it. A panel sharing a card with another is not separately on screen —
    /// they are tabs of one thing — so putting one away while the other stayed would mean closing half a card,
    /// which is not a state the window can be in. Opening is per panel, because a panel that is away has no card
    /// to share and no company to bring with it.
    /// </remarks>
    private static void TogglePanel(PanelId panel) => Arrangement.Apply(layout =>
    {
        if (!layout.IsOpen(panel))
            return layout.Move(panel, PanelRegistry.DefaultSideOf(panel), int.MaxValue);

        DockLayout closed = layout;

        foreach (PanelId member in DockGrouping.SharingACard(layout, panel))
            closed = closed.Close(member);

        return closed;
    });

    /// <summary>Lights the icon of every panel that is on screen.</summary>
    /// <remarks>
    /// The same rule the pen and the eye follow — lit means on — so the strip needs no labels and no second kind
    /// of indicator for the reader to learn.
    /// </remarks>
    private void ShowState()
    {
        foreach (Button button in host.Children.OfType<Border>().SelectMany(Icons))
        {
            if (button.Tag is not PanelId panel || button.Content is not Path glyph)
                continue;

            glyph.Stroke = (Brush)host.FindResource(Arrangement.Current.IsOpen(panel) ? ThemeKeys.Accent : ThemeKeys.TextSecondary);
        }
    }

    private static IEnumerable<Button> Icons(Border group)
        => group.Child is StackPanel row ? row.Children.OfType<Button>() : [];
}
