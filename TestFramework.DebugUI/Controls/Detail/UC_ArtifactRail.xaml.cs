using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using Axiom.State;
using Axiom.Wpf.Extensions;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// Each artifact and the versions captured of it.
/// </summary>
/// <remarks>
/// The version chain is what makes an artifact legible: a row that was set up, changed twice and
/// then cleaned reads as a history rather than as a single current value. None of this was possible
/// before Core started publishing version changes — the events simply never left the run.
/// </remarks>
public partial class UC_ArtifactRail : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    /// <summary>Creates the rail and binds it.</summary>
    public UC_ArtifactRail()
    {
        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun.Artifacts)
            .Select(artifacts => (IEnumerable<ArtifactNode>)[.. artifacts.Values.OrderBy(artifact => artifact.Key, StringComparer.Ordinal)])
            .BindToCollection(spArtifacts.Children, artifact => artifact.Key, artifact => new UC_ArtifactItem(artifact.Key)));

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun.Artifacts.Count == 0)
            .Select(empty => empty ? Visibility.Visible : Visibility.Collapsed)
            .BindToDependencyProperty(tbEmpty, VisibilityProperty));

        Unloaded += (_, _) => subscriptions.Dispose();
    }
}
