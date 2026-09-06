using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using TestFramework.DebugUI.Docking;

using TestFramework.DebugUI.State.Transport;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// Draws the arrangement.
/// </summary>
/// <remarks>
/// <para>
/// The arrangement is the truth and this renders it, never the other way round — the same call the board makes
/// about its own geometry. It means the whole thing can be arranged, saved and reasoned about by a test that
/// never opens a window, and that the file on disk and what is on screen cannot come to disagree.
/// </para>
/// <para>
/// Two rules do most of the work here. <em>Panels are reparented, never rebuilt</em>: a panel carries a scroll
/// position, an expanded stack trace and whatever was typed into its filter, and recreating it on every move
/// would throw all of that away. And <em>shape rebuilds, size does not</em>: dragging a splitter changes a
/// column width and nothing else, because rebuilding forty times a second would do exactly the throwing away
/// the first rule exists to prevent.
/// </para>
/// </remarks>
public partial class UC_DockHost : UserControl
{
    /// <summary>The least of the window the canvas keeps, whatever the wells ask for.</summary>
    private const double MinimumMiddle = 0.05;

    /// <summary>
    /// Every panel, built once and kept.
    /// </summary>
    /// <remarks>
    /// Built eagerly at startup rather than when first shown. They are cheap, the window wires events on them
    /// once, and a closed panel that keeps its subscriptions is a closed panel that reopens showing what the
    /// reader left in it.
    /// </remarks>
    private readonly Dictionary<PanelId, UserControl> panels = [];

    /// <summary>The card each panel is currently drawn in, so a drag can ask where it is.</summary>
    private readonly Dictionary<PanelId, FrameworkElement> faces = [];

    /// <summary>The stack each side is drawn in, so a division can be applied without rebuilding it.</summary>
    private readonly Dictionary<DockSide, Grid> rails = [];

    /// <summary>What is currently drawn, so a change that is only a resize can be told from one that is not.</summary>
    private DockLayout? drawn;

    /// <summary>Moving a panel from one place to another, and the hint that says where it would land.</summary>
    private readonly DockDragController drag;

    /// <summary>The panels that have been dragged out into windows of their own.</summary>
    private readonly DockFloats floats;

    /// <summary>The edges of the wells, and how much of the window each side has.</summary>
    private readonly DockSplitters splitters;

    /// <summary>Creates the host and draws the current arrangement.</summary>
    public UC_DockHost()
    {
        InitializeComponent();

        // Handed what it cannot know and nothing else: where a drag is measured, what is drawn, and how
        // to set the stars on this host's own grid.
        splitters = new DockSplitters(this, () => drawn, Weigh);
        splitters.InsetsChanged += insets => InsetsChanged?.Invoke(insets);

        drag = new DockDragController(this, gDropHint) { Refresh = Remember };

        // The float knows how a window behaves; the chrome inside it is the host's, because a floated
        // panel wears the same header a docked one does.
        floats = new DockFloats(this, drag, panel => panels[panel], panel => Header(panel, active: true, draggable: false), Strip);

        Arrangement.Changed += Show;

        // No handler for the window changing size. The extents are star weights, so the layout system rescales
        // every well on its own — and a size written from inside a size-changed handler is exactly the loop that
        // sent the first version of this to 2380 pixels inside a 1000-pixel window.
        Loaded += (_, _) => Show();

        // Card positions are only true once the layout has run, and they change with every resize as well as
        // every rearrangement — so they are refreshed from the one event that covers both.
        LayoutUpdated += (_, _) => Remember();


        Unloaded += (_, _) => Arrangement.Changed -= Show;
    }

    /// <summary>Raised with what fraction of the window the pinned wells have taken, for the canvas to keep out of.</summary>
    /// <remarks>
    /// The board is a sibling rather than a child, so that it stays independent of all of this and its own
    /// fit-to-window works against the area it actually has. This is the one thing it needs to be told, and it is
    /// told in fractions so that nothing in the host has to read a size.
    /// </remarks>
    public event Action<DockInsets>? InsetsChanged;

    /// <summary>
    /// Builds the panels, giving each of them what it can ask the shell to do.
    /// </summary>
    /// <remarks>
    /// Not the constructor, because this host is declared in the window's markup and WPF builds those with
    /// no arguments. So the window hands the panels their collaborator the moment it has one, and this is
    /// the one place in the tool where something has to happen before something else — the panels
    /// themselves take it in their constructors and cannot be built without it.
    /// </remarks>
    public void UseCommands(IShellCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (panels.Count > 0)
            return;

        foreach (PanelDescriptor descriptor in PanelRegistry.All)
            panels[descriptor.Id] = descriptor.Create(commands);

        Show();
    }

    /// <summary>One panel, for the window to wire its events to.</summary>
    public T Get<T>(PanelId panel) where T : UserControl
        => panels.TryGetValue(panel, out UserControl? found)
            ? (T)found
            : throw new InvalidOperationException($"The panels have not been built yet; {nameof(UseCommands)} comes first.");

    /// <summary>Draws the arrangement, rebuilding only what has actually changed shape.</summary>
    private void Show()
    {
        DockLayout layout = Arrangement.Current;

        if (drawn is not null && drawn.SameShapeAs(layout))
        {
            drawn = layout;

            // The floats are still reconciled: this is the path a float's own move takes, and it is also the path
            // the first pass after the window opens takes — which is when the windows deferred at startup appear.
            floats.Show(layout);

            // And the divisions are re-applied. Dragging a boundary is not a shape change, so it comes through
            // here rather than through a rebuild — and without this the rows would only ever be right because the
            // drag happened to have previewed them, leaving any other way of dividing a well with no effect.
            foreach (KeyValuePair<DockSide, Grid> rail in rails)
                DockSplitters.Preview(rail.Value, layout.At(rail.Key).Shares);

            splitters.Apply();
            return;
        }

        drawn = layout;

        // Every panel is taken out of whatever holds it before anything is put anywhere. Clearing the grid is not
        // enough and this is not a tidiness measure: a panel sits several levels down, inside the well's chrome,
        // so emptying the top only unparents the chrome and WPF then refuses the panel with "already the logical
        // child of another element" halfway through the rebuild, leaving the window blank.
        foreach (UserControl panel in panels.Values)
            PanelActions.Detach(panel);

        gWells.Children.Clear();
        rails.Clear();

        foreach (DockSide side in new[] { DockSide.Left, DockSide.Right, DockSide.Bottom, DockSide.Center })
            Place(side, layout.At(side));

        floats.Show(layout);

        splitters.Apply();
    }

    /// <summary>The tab strip for a float holding more than one panel.</summary>
    private StackPanel Strip(DockFloat afloat)
    {
        StackPanel strip = new() { Orientation = Orientation.Horizontal };

        foreach (PanelId panel in afloat.Panels)
        {
            strip.Children.Add(Tab(panel, active: afloat.Active == panel));

            if (panels[panel] is IPanelActions offering)
                offering.ReclaimActions();
        }

        return strip;
    }

    /// <summary>Notes where every card ended up, which is what a drag is answered against.</summary>
    private void Remember() => drag.Remember(drawn, faces);

    /// <summary>
    /// Puts a well in its cell.
    /// </summary>
    /// <remarks>
    /// The rails run the full height and the bottom well sits between them rather than under them, which is where
    /// a panel across the bottom goes in every tool that has one: it belongs to the canvas, not to the window.
    /// The centre shares the canvas's cell and covers it.
    /// </remarks>
    private void Place(DockSide side, DockWell well)
    {
        if (well.IsEmpty)
            return;

        FrameworkElement container = Well(side, well);

        switch (side)
        {
            case DockSide.Left:
                Grid.SetColumn(container, 0);
                Grid.SetRowSpan(container, 2);
                break;

            case DockSide.Right:
                Grid.SetColumn(container, 2);
                Grid.SetRowSpan(container, 2);
                break;

            case DockSide.Bottom:
                Grid.SetColumn(container, 1);
                Grid.SetRow(container, 1);
                break;

            default:
                Grid.SetColumn(container, 1);
                Grid.SetRow(container, 0);

                // Above the canvas it shares a cell with, because a page is the thing in front.
                Panel.SetZIndex(container, 1);
                break;
        }

        gWells.Children.Add(container);
    }

    /// <summary>
    /// One well: the cards in it, and the edge that sizes it.
    /// </summary>
    /// <remarks>
    /// The well itself draws nothing. It is a region that lays cards out and carries the handle for its edge —
    /// every surface a reader can see belongs to a panel, so a well holding two panels reads as two things rather
    /// than as one thing divided.
    /// </remarks>
    private FrameworkElement Well(DockSide side, DockWell well)
    {
        FrameworkElement inner = side == DockSide.Center ? Tabbed(well) : Stacked(side, well);

        if (side == DockSide.Center)
            return inner;

        // The grip goes on the well's inner edge — the one facing the canvas — because that is the edge a
        // reader drags to give the board more or less room.
        Grid withGrip = new();
        withGrip.Children.Add(inner);
        withGrip.Children.Add(splitters.Grip(side));

        return withGrip;
    }

    /// <summary>
    /// One panel as a card that floats free of everything around it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing docked shares an edge with anything else. Every card carries the same inset on all four sides, so
    /// two cards side by side are separated by twice it and a card against the window by once — which is what
    /// makes an arrangement read as panels laid over the canvas rather than as a frame carved into regions. It
    /// also means a well never draws a divider, because there is nothing left to divide.
    /// </para>
    /// <para>
    /// The lift comes from a second border behind the card rather than an effect on the card itself. An
    /// <c>Effect</c> renders its whole subtree to a bitmap on every change, and these subtrees hold a board's
    /// worth of live content; a shadow cast by an empty sibling of the same shape costs nothing to redraw.
    /// </para>
    /// </remarks>
    private FrameworkElement Card(PanelId panel, UIElement header, UIElement content)
    {

        DropShadowEffect shadow = new()
        {
            // No offset: a card lifted straight off the surface rather than lit from a corner. Depth without
            // a direction is what stops several of them reading as sheets of paper on a desk.
            ShadowDepth = 0,
            BlurRadius = 18,
            Opacity = 0.55
        };

        // Not black. A light theme's shadow is a low cool grey, and PipeShadow is where that already lives.
        ThemeFollow.Shadow(shadow, ThemeKeys.PipeShadow);

        Border lift = new()
        {
            CornerRadius = new CornerRadius(DockMetrics.CardRadius),
            Background = (Brush)FindResource(ThemeKeys.SurfaceOverlay),
            Effect = shadow
        };

        Grid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(content, 1);
        body.Children.Add(header);
        body.Children.Add(content);

        Brush faceBrush = (Brush)FindResource(ThemeKeys.SurfaceOverlay);

        Border face = new()
        {
            CornerRadius = new CornerRadius(DockMetrics.CardRadius),
            Background = faceBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource(ThemeKeys.PanelEdge),
            Child = body
        };

        // Clipped to its own corners, so a scrollbar or a row of tabs cannot square them off again. Recomputed
        // when the card's size changes — a read of a size, but it sets a clip rather than a size, so there is
        // nothing here for the layout to feed back into.
        face.SizeChanged += (_, e) => face.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
            DockMetrics.CardRadius,
            DockMetrics.CardRadius);

        faces[panel] = face;

        Grid card = new() { Margin = new Thickness(DockMetrics.CardGap) };
        card.Children.Add(lift);
        card.Children.Add(face);

        return card;
    }

    /// <summary>Cards one above the next, as a rail.</summary>
    /// <remarks>
    /// A card each rather than one card in sections, which is the whole difference between a rail that reads as
    /// two panels and a rail that reads as one panel with a line across it. Equal shares between them: how a well
    /// divides itself is not remembered yet, and half each is what the fixed layout gave them.
    /// </remarks>
    private Grid Stacked(DockSide side, DockWell well)
    {
        Grid rail = new();
        rails[side] = rail;

        ImmutableList<double> shares = well.Shares;

        for (int index = 0; index < well.Panels.Count; index++)
        {
            PanelId panel = well.Panels[index];

            rail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(shares[index], GridUnitType.Star) });

            FrameworkElement card = Card(panel, Header(panel, active: well.Active == panel), panels[panel]);

            Grid.SetRow(card, index);
            rail.Children.Add(card);

            // A handle in the gap between this card and the one above it. In the gap rather than in a row of its
            // own: the cards already stand apart, and giving the handle a row would widen that gap every time a
            // panel was added.
            if (index > 0)
                rail.Children.Add(splitters.Boundary(side, rail, index - 1));
        }

        return rail;
    }

    /// <summary>A tab strip, and the one panel it has chosen, in a single card.</summary>
    /// <remarks>
    /// One card here rather than one per panel, because tabs are how several panels share one card: what goes in
    /// the centre wants the whole area — a table six columns wide, a value in full — and two of them splitting it
    /// would leave neither enough to be worth opening.
    /// </remarks>
    private FrameworkElement Tabbed(DockWell well)
    {
        StackPanel strip = new() { Orientation = Orientation.Horizontal };

        foreach (PanelId panel in well.Panels)
        {
            strip.Children.Add(Tab(panel, active: well.Active == panel));

            // Put back inside the panel: a tab strip belongs to several panels at once, so a button in it would
            // read as applying to all of them.
            if (panels[panel] is IPanelActions offering)
                offering.ReclaimActions();
        }

        UIElement content = well.Active is { } shown ? panels[shown] : new Grid();

        return Card(well.Active ?? PanelId.Home, strip, content);
    }

    /// <summary>A stacked panel's header: what it is, and the way to put it away.</summary>
    private UIElement Header(PanelId panel, bool active, bool draggable = true)
    {
        PanelDescriptor descriptor = PanelRegistry.Of(panel);

        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock title = new()
        {
            Text = descriptor.Title,
            Foreground = (Brush)FindResource(active ? ThemeKeys.TextSecondary : ThemeKeys.TextFaint),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        // Reload, share, import: the panel's own doing, beside the way out of it, which is where every window
        // in every tool keeps the things it does. They only come up here when the panel has a header to itself —
        // in a tab strip they would look like they applied to every tab in it.
        if (panels[panel] is IPanelActions offering)
        {
            PanelActions.Detach(offering.Actions);

            Grid.SetColumn(offering.Actions, 1);
            row.Children.Add(offering.Actions);
        }

        Button close = CloseButton(panel);
        Grid.SetColumn(close, 2);

        row.Children.Add(title);
        row.Children.Add(close);

        Border header = new()
        {
            Height = 26,
            Background = Brushes.Transparent,
            Child = row,

            // A hand only where the header is something to press. In a floating window it is the window's caption:
            // it moves the window, the system draws the cursor for that, and a hand would promise the wrong thing.
            Cursor = draggable ? Cursors.Hand : null
        };

        // Clicking a header is asking to look at that panel, which in a stack means it takes the attention its
        // title is drawn in. The panels are all visible either way; this is what the title bar's strip lights.
        // Left alone in a float, where the same press belongs to the window. Both gestures on one strip meant the
        // window could not be moved at all, and a popped-out panel is a window first.
        if (draggable)
        {
            header.MouseLeftButtonUp += (_, _) => Arrangement.Apply(layout => layout.Activate(panel));

            drag.MakeDraggable(header, panel);
        }

        return header;
    }

    /// <summary>One tab in the centre strip.</summary>
    private UIElement Tab(PanelId panel, bool active)
    {
        PanelDescriptor descriptor = PanelRegistry.Of(panel);

        StackPanel content = new() { Orientation = Orientation.Horizontal };

        content.Children.Add(new TextBlock
        {
            Text = descriptor.Title,
            Foreground = (Brush)FindResource(active ? ThemeKeys.TextPrimary : ThemeKeys.TextFaint),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 4, 0)
        });

        Button close = CloseButton(panel);
        content.Children.Add(close);

        Border tab = new()
        {
            Height = 28,
            Background = active ? (Brush)FindResource(ThemeKeys.SurfaceOverlay) : Brushes.Transparent,

            // Only the chosen tab carries the accent, and only along its top edge: a whole tab in the accent
            // would compete with everything inside the panel it opens.
            BorderBrush = active ? (Brush)FindResource(ThemeKeys.Accent) : Brushes.Transparent,
            BorderThickness = new Thickness(0, 2, 0, 0),
            Cursor = Cursors.Hand,
            Child = content
        };

        tab.MouseLeftButtonUp += (_, _) => Arrangement.Apply(layout => layout.Activate(panel));

        // Marked on the tab itself, not only on the strip that holds it: a floating window puts its strip inside
        // the caption, and the chrome asks the element it actually hit — so without this the chrome takes the
        // press to drag the window and a panel can never be dragged back out of a float.
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(tab, true);

        drag.MakeDraggable(tab, panel);

        return tab;
    }

    private Button CloseButton(PanelId panel)
    {
        Button close = new()
        {
            Style = (Style)FindResource(ThemeKeys.IconButton),
            Margin = new Thickness(0, 2, 4, 2),
            ToolTip = "Close this panel",
            Content = new Path
            {
                Data = (Geometry)FindResource(ThemeKeys.IconClose),
                Stroke = (Brush)FindResource(ThemeKeys.TextFaint),
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 18,
                Height = 18,
                Stretch = Stretch.None
            }
        };

        close.Click += (_, _) => Arrangement.Apply(layout => layout.Close(panel));

        return close;
    }

    /// <summary>
    /// Sets the three weights, and gives the middle what is left.
    /// </summary>
    /// <remarks>
    /// The middle keeps a floor rather than being allowed to reach zero: the extents are each capped at half the
    /// window, but a left and a right well both at their maximum would together take all of it and leave the
    /// canvas nothing to be.
    /// </remarks>
    private void Weigh(double left, double right, double bottom)
    {
        cLeft.Width = new GridLength(left, GridUnitType.Star);
        cRight.Width = new GridLength(right, GridUnitType.Star);
        cMiddle.Width = new GridLength(Math.Max(MinimumMiddle, 1 - left - right), GridUnitType.Star);

        rBottom.Height = new GridLength(bottom, GridUnitType.Star);
        rMiddle.Height = new GridLength(Math.Max(MinimumMiddle, 1 - bottom), GridUnitType.Star);
    }
}
