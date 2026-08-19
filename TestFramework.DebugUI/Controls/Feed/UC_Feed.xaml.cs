using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Feed;

/// <summary>
/// What every run is doing, including the ones not on screen.
/// </summary>
/// <remarks>
/// <para>
/// A window that ingests many runs but renders one leaves everything happening in the others invisible. This is
/// where they speak: started, finished, failed, paused — plus anything the tool itself could not do.
/// </para>
/// <para>
/// Opened from the bell in the title bar, which carries the unread count and the colour of the worst thing in
/// it. This used to be a tab in the corner of the canvas that carried both itself, which meant the board always
/// had a plate on it saying something nobody had asked about.
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

        // An empty panel with a heading and nothing under it reads as a panel that failed to load.
        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Shell.Feed.Count)
            .Subscribe(ShowCount));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Raised when the reader closes the panel, so the bell can go back to unpressed.</summary>
    public event Action? Closed;

    /// <summary>
    /// Whether the panel is up.
    /// </summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>
    /// Opens the panel, and marks what it holds as read.
    /// </summary>
    /// <remarks>
    /// Read on opening rather than on closing: the count answers "is there anything I have not seen", and
    /// something the reader has just had in front of them has been seen.
    /// </remarks>
    public void Open()
    {
        Visibility = Visibility.Visible;
        StateStore<MainState>.Default.Dispatch(RunActions.ClearUnreadFeed);
    }

    /// <summary>Puts the panel away.</summary>
    public void Close()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    /// <summary>Opens the panel, or puts it away when it is already up.</summary>
    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    private void ShowCount(int count)
    {
        tbEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        svEntries.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;

        tbCount.Text = count switch
        {
            0 => string.Empty,
            1 => "1 entry",
            _ => string.Format(CultureInfo.CurrentCulture, "{0} entries", count)
        };
    }

    private void btClose_Click(object sender, RoutedEventArgs e) => Close();
}
