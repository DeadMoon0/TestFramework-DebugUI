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
    /// <summary>How thick the draggable edge of a well is.</summary>
    /// <remarks>
    /// Six, straddling the boundary, for the reason the step panel's own handle already gives: half over the
    /// panel and half over what is beside it is where a pointer aiming at an edge actually ends up.
    /// </remarks>
    private const double GripThickness = 6;

    /// <summary>The least of the window the canvas keeps, whatever the wells ask for.</summary>
    private const double MinimumMiddle = 0.05;

    /// <summary>The inset every card carries on all four sides.</summary>
    /// <remarks>
    /// One number, so the gap between two cards is always twice the gap to the window and the whole arrangement
    /// sits on one rhythm. Nothing docked ever touches anything else.
    /// </remarks>
    private const double CardGap = 7;

    /// <summary>How round a card's corners are.</summary>
    private const double CardRadius = 8;

    /// <summary>How far the pointer travels on a header before it counts as a drag rather than a click.</summary>
    /// <remarks>
    /// Clicking a header reveals its panel and dragging it moves the panel, so one gesture has to be told from the
    /// other. Below this it is a click, which is why a slightly unsteady press still selects rather than moves.
    /// </remarks>
    private const double DragThreshold = 6;

    /// <summary>The clipboard format a dragged panel travels under.</summary>
    /// <remarks>
    /// Named for this application, so a drag from somewhere else cannot be mistaken for one of ours — and so ours
    /// is refused by everything else.
    /// </remarks>
    private const string PanelFormat = "TestFramework.DebugUI.Panel";

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

    /// <summary>Every card on screen, collected as they are drawn so a drag has an answer for every pixel.</summary>
    private readonly List<DockCard> cards = [];

    /// <summary>Where the pointer went down on a header, until it has moved far enough to mean a drag.</summary>
    private Point? pressedAt;

    /// <summary>
    /// One window per floating card, matched to the arrangement by position.
    /// </summary>
    /// <remarks>
    /// Matched by position rather than kept in a dictionary keyed on the float: a float is a record whose value
    /// changes every time the window is nudged, so it cannot be its own key. The arrangement keeps its floats in a
    /// stable order and this list follows it.
    /// </remarks>
    private readonly List<DockFloatWindow> windows = [];

    private DockSide sizing;
    private bool isSizing;
    private double sizingFrom;
    private double sizingExtent;

    /// <summary>Creates the host and draws the current arrangement.</summary>
    public UC_DockHost()
    {
        InitializeComponent();

        foreach (PanelDescriptor descriptor in PanelRegistry.All)
            panels[descriptor.Id] = descriptor.Create();

        Arrangement.Changed += Show;

        // The host takes the drop rather than each card, because most of the answers are about the space between
        // cards and the edges of the window — places no card owns.
        AllowDrop = true;
        DragOver += WhileDragging;
        Drop += OnDropped;
        DragLeave += (_, _) => ClearHint();

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

    /// <summary>One panel, for the window to wire its events to.</summary>
    public T Get<T>(PanelId panel) where T : UserControl => (T)panels[panel];

    /// <summary>Draws the arrangement, rebuilding only what has actually changed shape.</summary>
    private void Show()
    {
        DockLayout layout = Arrangement.Current;

        if (drawn is not null && drawn.SameShapeAs(layout))
        {
            drawn = layout;

            // The floats are still reconciled: this is the path a float's own move takes, and it is also the path
            // the first pass after the window opens takes — which is when the windows deferred at startup appear.
            ShowFloats(layout);

            // And the divisions are re-applied. Dragging a boundary is not a shape change, so it comes through
            // here rather than through a rebuild — and without this the rows would only ever be right because the
            // drag happened to have previewed them, leaving any other way of dividing a well with no effect.
            foreach (KeyValuePair<DockSide, Grid> rail in rails)
                Preview(rail.Value, layout.At(rail.Key).Shares);

            Size();
            return;
        }

        drawn = layout;

        // Every panel is taken out of whatever holds it before anything is put anywhere. Clearing the grid is not
        // enough and this is not a tidiness measure: a panel sits several levels down, inside the well's chrome,
        // so emptying the top only unparents the chrome and WPF then refuses the panel with "already the logical
        // child of another element" halfway through the rebuild, leaving the window blank.
        foreach (UserControl panel in panels.Values)
            Detach(panel);

        gWells.Children.Clear();
        cards.Clear();
        rails.Clear();

        foreach (DockSide side in new[] { DockSide.Left, DockSide.Right, DockSide.Bottom, DockSide.Center })
            Place(side, layout.At(side));

        ShowFloats(layout);

        Size();
    }

    /// <summary>
    /// Brings the floating windows into line with the arrangement.
    /// </summary>
    /// <remarks>
    /// The windows are built from the arrangement like everything else, which is what keeps a float from being a
    /// second source of truth about where a panel is. A float that has emptied has already been removed from the
    /// arrangement by the model, so there is no case here for "a window with nothing in it" — it simply has no
    /// float to match and is closed.
    /// </remarks>
    private void ShowFloats(DockLayout layout)
    {
        Window? owner = Window.GetWindow(this);

        // Not until the main window has been shown. WPF refuses an Owner that has never been shown, and a saved
        // arrangement is restored before the window opens — so creating a float there threw, and the exception
        // left the application half-built with no window at all. The Loaded pass below comes back for them.
        if (owner is null || !owner.IsLoaded)
            return;

        while (windows.Count > layout.Floats.Count)
        {
            DockFloatWindow extra = windows[^1];
            windows.RemoveAt(windows.Count - 1);

            // Emptied before closing: a panel still parented into a window that is closing would go down with it.
            extra.ShowContent(new Grid(), new Grid());
            extra.Close();
        }

        while (windows.Count < layout.Floats.Count)
            windows.Add(Adopt(owner, windows.Count));

        for (int index = 0; index < layout.Floats.Count; index++)
        {
            DockFloat afloat = layout.Floats[index];
            DockFloatWindow window = windows[index];

            // One panel is not a set of tabs. A window holding a single panel gets the same header a docked one
            // has — its name, its buttons, its close — because it is the same thing, just in a window of its own.
            // Tabs appear only once a second panel has been dragged in, which is the point at which they mean
            // something.
            UIElement caption = afloat.Panels.Count == 1
                ? Header(afloat.Panels[0], active: true)
                : Strip(afloat);

            window.ShowContent(caption, afloat.Active is { } shown ? panels[shown] : new Grid());

            // Shown before it is placed. A window that has never been shown has no handle, and the placement goes
            // through Win32 — so doing it the other way round put every float at whatever position WPF cascades
            // to and quietly dropped the one the panel was released at.
            if (!window.IsVisible)
                window.Show();

            window.PlaceAt(afloat.Bounds);
        }
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

    /// <summary>Creates a floating window and wires what it reports back to the arrangement.</summary>
    private DockFloatWindow Adopt(Window owner, int index)
    {
        DockFloatWindow window = new(owner);

        // A float takes drops like a well does, which is what lets a panel be dragged from the main window into a
        // window that is already floating. There are no zones inside one: a float is a single tabbed card, so a
        // drop anywhere in it joins the strip.
        window.AllowDrop = true;
        window.DragOver += (_, e) =>
        {
            e.Effects = Carried(e) is null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
        };

        window.Drop += (_, e) =>
        {
            if (Carried(e) is not { } dragged)
                return;

            e.Handled = true;

            int at = windows.IndexOf(window);

            if (at >= 0)
                Arrangement.Apply(layout => layout.MoveIntoFloat(dragged, at, int.MaxValue));
        };

        window.Moved += bounds =>
        {
            int at = windows.IndexOf(window);

            if (at >= 0)
                Arrangement.Apply(layout => layout.MoveFloat(at, bounds));
        };

        window.CloseRequested += () =>
        {
            int at = windows.IndexOf(window);

            if (at < 0)
                return;

            Arrangement.Apply(layout =>
            {
                DockLayout closed = layout;

                foreach (PanelId panel in layout.Floats[at].Panels)
                    closed = closed.Close(panel);

                return closed;
            });
        };

        return window;
    }

    /// <summary>
    /// Notes where every card ended up, once the layout has actually placed them.
    /// </summary>
    /// <remarks>
    /// After layout rather than during it: a card has no position until it has been arranged, and a drag needs
    /// positions rather than the tree it could work them out from. Recorded once per redraw and read on every
    /// mouse move, which is the right way round for the thing that happens thousands of times.
    /// </remarks>
    private void Remember()
    {
        cards.Clear();

        if (drawn is null || ActualWidth <= 0)
            return;

        foreach (KeyValuePair<PanelId, FrameworkElement> held in faces)
        {
            if (!held.Value.IsVisible)
                continue;

            DockLocation at = drawn.Locate(held.Key);

            if (at.Placement != DockPlacement.Side)
                continue;

            try
            {
                Point corner = held.Value.TransformToAncestor(this).Transform(new Point(0, 0));

                cards.Add(new DockCard
                {
                    Panel = held.Key,
                    Side = at.Side,
                    Index = at.Index,
                    Bounds = new Rect(corner, new Size(held.Value.ActualWidth, held.Value.ActualHeight))
                });
            }
            catch (InvalidOperationException)
            {
                // Not in this visual tree yet. It will be by the next redraw, and a drag before then simply
                // finds one card fewer rather than failing.
            }
        }
    }

    /// <summary>
    /// Takes a panel out of whatever is holding it, whatever kind of thing that is.
    /// </summary>
    /// <remarks>
    /// Every kind, and that is the point. A docked panel sits in a grid, so removing it from
    /// <c>Panel.Children</c> was enough while floats did not exist — but a floated one is a border's single child,
    /// which is not a <see cref="Panel"/> at all, so it was silently skipped. WPF then refused it as "already the
    /// logical child of another element" partway through the rebuild, which aborted with some panels reparented
    /// and the rest nowhere: every tool window in the application disappeared at once, and the only way to get
    /// them back was to restart. Dragging a panel out of a float and back in is exactly the gesture that hit it.
    /// </remarks>
    private static void Detach(UserControl panel)
    {
        switch (panel.Parent)
        {
            case Panel holder:
                holder.Children.Remove(panel);
                break;

            // A Border is a Decorator, which is how a float holds its panel.
            case Decorator decorator:
                decorator.Child = null;
                break;

            case ContentControl content:
                content.Content = null;
                break;

            default:
                break;
        }
    }

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
        withGrip.Children.Add(Grip(side));

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
        Border lift = new()
        {
            CornerRadius = new CornerRadius(CardRadius),
            Background = (Brush)FindResource("SurfaceOverlay"),
            Effect = new DropShadowEffect
            {
                // No offset: a card lifted straight off the surface rather than lit from a corner. Depth without
                // a direction is what stops several of them reading as sheets of paper on a desk.
                ShadowDepth = 0,
                BlurRadius = 18,
                Opacity = 0.55,
                Color = Colors.Black
            }
        };

        Grid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(content, 1);
        body.Children.Add(header);
        body.Children.Add(content);

        Border face = new()
        {
            CornerRadius = new CornerRadius(CardRadius),
            Background = (Brush)FindResource("SurfaceOverlay"),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("PanelEdge"),
            Child = body
        };

        // Clipped to its own corners, so a scrollbar or a row of tabs cannot square them off again. Recomputed
        // when the card's size changes — a read of a size, but it sets a clip rather than a size, so there is
        // nothing here for the layout to feed back into.
        face.SizeChanged += (_, e) => face.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
            CardRadius,
            CardRadius);

        faces[panel] = face;

        Grid card = new() { Margin = new Thickness(CardGap) };
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
                rail.Children.Add(Boundary(side, rail, index - 1));
        }

        return rail;
    }

    /// <summary>
    /// The handle between two stacked panels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Straddles the boundary, half over each card, because that is where a pointer aiming at the space between
    /// two things actually ends up. Invisible until it is worth noticing: a permanent seam between cards would
    /// undo the point of drawing them as separate things in the first place.
    /// </para>
    /// <para>
    /// Moves the rows while it is dragged and commits to the arrangement on release, for the same reason the
    /// well's own edge does — the arrangement is written to the settings file whenever it changes, and a file
    /// written forty times a second is a file written for no reason.
    /// </para>
    /// </remarks>
    private Border Boundary(DockSide side, Grid rail, int boundary)
    {
        Border grip = new()
        {
            Height = GripThickness * 2,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeNS,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(CardGap, -GripThickness, CardGap, 0),
            ToolTip = "Drag to share the space between these panels"
        };

        Grid.SetRow(grip, boundary + 1);

        double from = 0;
        double moved = 0;
        bool sizing = false;

        grip.MouseLeftButtonDown += (_, e) =>
        {
            sizing = true;
            moved = 0;
            from = e.GetPosition(rail).Y;
            grip.CaptureMouse();
        };

        grip.MouseMove += (_, e) =>
        {
            if (!sizing || rail.ActualHeight <= 0)
                return;

            double now = e.GetPosition(rail).Y;

            moved += (now - from) / rail.ActualHeight;
            from = now;

            Preview(rail, Arrangement.Current.At(side).Divide(boundary, moved).Shares);
        };

        grip.MouseLeftButtonUp += (_, _) =>
        {
            if (!sizing)
                return;

            sizing = false;
            grip.ReleaseMouseCapture();

            double settled = moved;

            Arrangement.Apply(layout => layout.Divide(side, boundary, settled));
        };

        return grip;
    }

    /// <summary>Shows a division while it is being dragged, without touching the arrangement.</summary>
    private static void Preview(Grid rail, ImmutableList<double> shares)
    {
        for (int row = 0; row < rail.RowDefinitions.Count && row < shares.Count; row++)
            rail.RowDefinitions[row].Height = new GridLength(shares[row], GridUnitType.Star);
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
    private UIElement Header(PanelId panel, bool active)
    {
        PanelDescriptor descriptor = PanelRegistry.Of(panel);

        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock title = new()
        {
            Text = descriptor.Title,
            Foreground = (Brush)FindResource(active ? "TextSecondary" : "TextFaint"),
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
            Cursor = Cursors.Hand
        };

        // Clicking a header is asking to look at that panel, which in a stack means it takes the attention its
        // title is drawn in. The panels are all visible either way; this is what the title bar's strip lights.
        header.MouseLeftButtonUp += (_, _) => Arrangement.Apply(layout => layout.Activate(panel));

        MakeDraggable(header, panel);

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
            Foreground = (Brush)FindResource(active ? "TextPrimary" : "TextFaint"),
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
            Background = active ? (Brush)FindResource("SurfaceOverlay") : Brushes.Transparent,

            // Only the chosen tab carries the accent, and only along its top edge: a whole tab in the accent
            // would compete with everything inside the panel it opens.
            BorderBrush = active ? (Brush)FindResource("Accent") : Brushes.Transparent,
            BorderThickness = new Thickness(0, 2, 0, 0),
            Cursor = Cursors.Hand,
            Child = content
        };

        tab.MouseLeftButtonUp += (_, _) => Arrangement.Apply(layout => layout.Activate(panel));

        // Marked on the tab itself, not only on the strip that holds it: a floating window puts its strip inside
        // the caption, and the chrome asks the element it actually hit — so without this the chrome takes the
        // press to drag the window and a panel can never be dragged back out of a float.
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(tab, true);

        MakeDraggable(tab, panel);

        return tab;
    }

    /// <summary>
    /// Makes a header or a tab something a panel can be dragged by.
    /// </summary>
    /// <remarks>
    /// The header is where a window is dragged by everywhere else, so it is where a panel is dragged by here. The
    /// press is remembered rather than acted on, because the same press is also how a panel is revealed — only
    /// once the pointer has travelled does it become a move.
    /// </remarks>
    private void MakeDraggable(FrameworkElement handle, PanelId panel)
    {
        // Measured against the handle rather than the host: the same headers and tabs are drawn inside floating
        // windows, and asking for a position in another window's tree is not a question that has an answer.
        handle.PreviewMouseLeftButtonDown += (_, e) => pressedAt = e.GetPosition(handle);
        handle.PreviewMouseLeftButtonUp += (_, _) => pressedAt = null;

        handle.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || pressedAt is not { } from)
                return;

            Point now = e.GetPosition(handle);

            if (Math.Abs(now.X - from.X) < DragThreshold && Math.Abs(now.Y - from.Y) < DragThreshold)
                return;

            pressedAt = null;


            Remember();

            DataObject carried = new(PanelFormat, panel);

            // Blocks until the drag ends, which is what lets the arrangement be changed exactly once, on release.
            DragDropEffects taken = DragDrop.DoDragDrop(handle, carried, DragDropEffects.Move);

            ClearHint();

            // Nothing accepted it. That is either a drop into empty space — which means a window of its own — or a
            // drag the reader abandoned with Escape, and the two are told apart by where the pointer ended up
            // rather than by the result, because cancelling reports exactly the same thing as dropping nowhere.
            if (taken == DragDropEffects.None && OutsideEveryWindow(out Point where))
                Arrangement.Apply(layout => layout.Float(panel, Near(where)));
        };
    }

    /// <summary>Shows where the panel under the pointer would land.</summary>
    private void WhileDragging(object sender, DragEventArgs e)
    {
        if (Carried(e) is not { } dragged)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        DockDrop? drop = DockDrop.Resolve(e.GetPosition(this), new Size(ActualWidth, ActualHeight), cards, dragged);


        ShowHint(drop);

        e.Effects = drop is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>Moves the panel to wherever the pointer let go.</summary>
    private void OnDropped(object sender, DragEventArgs e)
    {
        ClearHint();

        if (Carried(e) is not { } dragged)
            return;

        DockDrop? drop = DockDrop.Resolve(e.GetPosition(this), new Size(ActualWidth, ActualHeight), cards, dragged);


        if (drop is null)
            return;

        e.Handled = true;

        DockSide side = drop.Side;
        int index = drop.Index;

        Arrangement.Apply(layout => layout.Move(dragged, side, index));
    }

    /// <summary>
    /// Whether the pointer is outside every window of this application, and where.
    /// </summary>
    /// <remarks>
    /// Asked at the end of a drag nothing accepted. A pointer still over one of our windows means the reader
    /// cancelled — Escape reports the same "nothing took it" as a drop into the desktop — and cancelling must not
    /// leave a new window behind.
    /// </remarks>
    private static bool OutsideEveryWindow(out Point where)
    {
        where = NativeMethods.CursorPosition();

        foreach (Window window in Application.Current.Windows)
        {
            if (!window.IsVisible)
                continue;

            // Read from Win32 rather than from Left and Top, so the comparison happens in the same physical
            // pixels the pointer was reported in. The two spaces part company the moment a second monitor runs
            // at a different scale, which is precisely when somebody is dragging a panel onto it.
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out NativeMethods.RECT at))
                continue;

            Rect bounds = new(at.Left, at.Top, at.Right - at.Left, at.Bottom - at.Top);

            if (bounds.Contains(where))
                return false;
        }

        return true;
    }

    /// <summary>A float placed under the pointer rather than at its corner.</summary>
    /// <remarks>
    /// Offset by a little, so the new window appears where the panel was let go instead of hanging its title bar
    /// off the cursor — and so the pointer is over the window's body, ready to drag it again.
    /// </remarks>
    private static DockBounds Near(Point where) => new DockBounds
    {
        Left = where.X - 60,
        Top = where.Y - 14,
        Width = 480,
        Height = 560
    }.Sane();

    /// <summary>The panel a drag is carrying, or null when the drag is not one of ours.</summary>
    private static PanelId? Carried(DragEventArgs e)
        => e.Data?.GetDataPresent(PanelFormat) == true && e.Data.GetData(PanelFormat) is PanelId panel ? panel : null;

    /// <summary>
    /// Draws the hint, as a region to fill or a line to slot into.
    /// </summary>
    /// <remarks>
    /// Two shapes because they answer two different questions. A filled region says the panel will occupy this
    /// space; a line says it will go between these two. Drawing an insertion as a region would claim it was about
    /// to cover the card it is only going above.
    /// </remarks>
    private void ShowHint(DockDrop? drop)
    {
        gDropHint.Children.Clear();

        if (drop is null)
            return;

        Brush accent = (Brush)FindResource("Accent");

        Border hint = new()
        {
            Width = Math.Max(0, drop.Hint.Width),
            Height = Math.Max(0, drop.Hint.Height),
            CornerRadius = new CornerRadius(drop.IsInsertion ? 2 : CardRadius),
            Background = drop.IsInsertion ? accent : new SolidColorBrush(Color.FromArgb(0x30, 0x46, 0x80, 0xFC)),
            BorderBrush = drop.IsInsertion ? null : accent,
            BorderThickness = new Thickness(drop.IsInsertion ? 0 : 1.5)
        };

        Canvas.SetLeft(hint, drop.Hint.Left);
        Canvas.SetTop(hint, drop.Hint.Top);

        gDropHint.Children.Add(hint);
    }

    private void ClearHint() => gDropHint.Children.Clear();


    private Button CloseButton(PanelId panel)
    {
        Button close = new()
        {
            Style = (Style)FindResource("IconButton"),
            Margin = new Thickness(0, 2, 4, 2),
            ToolTip = "Close this panel",
            Content = new Path
            {
                Data = (Geometry)FindResource("IconClose"),
                Stroke = (Brush)FindResource("TextFaint"),
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
    /// The edge that sizes a well.
    /// </summary>
    /// <remarks>
    /// Committed to the arrangement when the pointer is released, not while it moves: the arrangement is written
    /// to the settings file whenever it changes, and a file written forty times a second is a file being written
    /// for no reason. The same call the step panel's own handle already made.
    /// </remarks>
    private Border Grip(DockSide side)
    {
        bool horizontal = side != DockSide.Bottom;

        Border grip = new()
        {
            Background = Brushes.Transparent,
            Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS,
            Width = horizontal ? GripThickness : double.NaN,
            Height = horizontal ? double.NaN : GripThickness,
            HorizontalAlignment = side switch
            {
                DockSide.Left => HorizontalAlignment.Right,
                DockSide.Right => HorizontalAlignment.Left,
                _ => HorizontalAlignment.Stretch
            },
            VerticalAlignment = side == DockSide.Bottom ? VerticalAlignment.Top : VerticalAlignment.Stretch
        };

        grip.MouseLeftButtonDown += (_, e) =>
        {
            isSizing = true;
            sizing = side;
            sizingExtent = Arrangement.Current.At(side).Extent;
            sizingFrom = horizontal ? e.GetPosition(this).X : e.GetPosition(this).Y;
            grip.CaptureMouse();
        };

        grip.MouseMove += (_, e) =>
        {
            if (!isSizing || sizing != side)
                return;

            double span = horizontal ? ActualWidth : ActualHeight;

            if (span <= 0)
                return;

            double moved = (horizontal ? e.GetPosition(this).X : e.GetPosition(this).Y) - sizingFrom;

            // Left grows to the right; right and bottom grow the other way.
            double delta = (side == DockSide.Left ? moved : -moved) / span;

            sizingExtent = Math.Clamp(sizingExtent + delta, DockWell.MinimumExtent, DockWell.MaximumExtent);
            sizingFrom = horizontal ? e.GetPosition(this).X : e.GetPosition(this).Y;

            Preview(side, sizingExtent);
        };

        grip.MouseLeftButtonUp += (_, _) =>
        {
            if (!isSizing)
                return;

            isSizing = false;
            grip.ReleaseMouseCapture();

            DockSide settled = sizing;
            double extent = sizingExtent;

            Arrangement.Apply(layout => layout.Resize(settled, extent));
        };

        return grip;
    }

    /// <summary>
    /// Gives every side its share, as a weight rather than a measurement.
    /// </summary>
    /// <remarks>
    /// A side with nothing in it weighs nothing, so an empty well takes no room without needing to be hidden. The
    /// middle takes what is left, and because these are all stars the whole thing rescales with the window on its
    /// own — nothing here reads a size, which is what stops this feeding back into the layout that called it.
    /// </remarks>
    private void Size()
    {
        if (drawn is null)
            return;

        Weigh(Share(DockSide.Left), Share(DockSide.Right), Share(DockSide.Bottom));

        InsetsChanged?.Invoke(Insets());
    }

    /// <summary>
    /// Moves one edge while it is being dragged, without touching the arrangement.
    /// </summary>
    /// <remarks>
    /// The arrangement is written to the settings file whenever it changes, so committing on every mouse move
    /// would write the file forty times a second. The drag moves the weights; releasing the pointer records where
    /// it ended up — the same call the step panel's own handle used to make.
    /// </remarks>
    private void Preview(DockSide side, double extent)
    {
        if (drawn is null)
            return;

        Weigh(
            side == DockSide.Left ? extent : Share(DockSide.Left),
            side == DockSide.Right ? extent : Share(DockSide.Right),
            side == DockSide.Bottom ? extent : Share(DockSide.Bottom));
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

    /// <summary>What fraction of the window a side takes, or nothing when it holds nothing.</summary>
    private double Share(DockSide side)
    {
        DockWell well = drawn?.At(side) ?? DockWell.Empty;

        return well.IsEmpty ? 0 : well.Extent;
    }

    /// <summary>
    /// How much the pinned wells have taken from the canvas.
    /// </summary>
    /// <remarks>
    /// A fraction of the window rather than pixels, applied by the window to the board's own margin. Reading a
    /// size here is what the first version got wrong, so this reports proportions and lets the caller turn them
    /// into a margin against a size it already has.
    /// </remarks>
    private DockInsets Insets() => new()
    {
        Left = Reserved(DockSide.Left),
        Right = Reserved(DockSide.Right),
        Bottom = Reserved(DockSide.Bottom)
    };

    private double Reserved(DockSide side)
    {
        DockWell well = drawn?.At(side) ?? DockWell.Empty;

        return well.Pinned && !well.IsEmpty ? well.Extent : 0;
    }
}

/// <summary>How much of the window the pinned wells have reserved, as fractions.</summary>
/// <remarks>
/// Fractions rather than a <c>Thickness</c> so that nothing in the host has to know how big anything is. The
/// window multiplies them by the area it is giving the board, which is a size it holds anyway.
/// </remarks>
public sealed record DockInsets
{
    /// <summary>Nothing reserved.</summary>
    public static DockInsets None { get; } = new();

    /// <summary>Gets the fraction reserved down the left.</summary>
    public double Left { get; init; }

    /// <summary>Gets the fraction reserved down the right.</summary>
    public double Right { get; init; }

    /// <summary>Gets the fraction reserved across the bottom.</summary>
    public double Bottom { get; init; }

    /// <summary>The margin these reserve inside an area of the given size.</summary>
    public Thickness Against(double width, double height)
        => new(Left * width, 0, Right * width, Bottom * height);
}
