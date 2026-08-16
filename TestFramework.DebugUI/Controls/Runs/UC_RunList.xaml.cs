using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.DebugUI.State;


namespace TestFramework.DebugUI.Controls.Runs;

/// <summary>
/// Every run this window knows about, live or recorded.
/// </summary>
/// <remarks>
/// This is the surface the whole journal exists for: a run that finished before the window was
/// opened is listed here beside the ones attached right now, and picking it replays it. What stood
/// here before was a label reading "Select Run" that could not be clicked.
/// </remarks>
public partial class UC_RunList : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    /// <summary>Creates the list and binds it.</summary>
    public UC_RunList()
    {
        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Runs)
            .Select(runs => (IEnumerable<RunSummary>)runs)
            .BindToCollection(spRuns.Children, run => run.SessionId, run => new UC_RunItem(run.SessionId)));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.Runs.Count)
            .Select(count => count == 0 ? Visibility.Visible : Visibility.Collapsed)
            .BindToDependencyProperty(tbEmpty, VisibilityProperty));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    private void btRefresh_Click(object sender, RoutedEventArgs e) => MainWindow.Shell.RefreshRecordedRuns();
}
