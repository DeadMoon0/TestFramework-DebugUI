using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Feed;

/// <summary>
/// What every run is doing, including the ones not on screen.
/// </summary>
/// <remarks>
/// <para>
/// A window that ingests many runs but renders one leaves everything happening in the others
/// invisible. This is where they speak: started, finished, failed, paused — plus anything the tool
/// itself could not do.
/// </para>
/// <para>
/// Collapsed by default with an unread count, because most of the time the run on screen is the
/// only one that matters and a permanently open log would just take space from the board.
/// </para>
/// </remarks>
public partial class UC_Feed : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    /// <summary>Creates the feed and binds it.</summary>
    public UC_Feed()
    {
        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Shell.Feed)
            .Select(entries => (IEnumerable<FeedEntry>)[.. entries.Reverse()])
            .BindToCollection(spEntries.Children, entry => entry, entry => new UC_FeedItem(entry)));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Shell.UnreadFeedCount)
            .Select(count => count == 0 ? Visibility.Collapsed : Visibility.Visible)
            .BindToDependencyProperty(bUnread, VisibilityProperty));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Shell.UnreadFeedCount)
            .Select(count => count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .BindToDependencyProperty(tbUnread, TextBlock.TextProperty));

        // The newest entry stays visible even when the feed is closed, so a collapsed feed still
        // says something rather than hiding everything behind a count.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Shell.Feed.Count == 0 ? string.Empty : state.Shell.Feed[^1].Title)
            .BindToDependencyProperty(tbLatest, TextBlock.TextProperty));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool opening = svEntries.Visibility != Visibility.Visible;

        svEntries.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;

        if (opening)
            StateStore<MainState>.Default.Dispatch(RunActions.ClearUnreadFeed);
    }
}
