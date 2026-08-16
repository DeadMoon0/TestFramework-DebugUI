using System;
using System.Globalization;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Axiom.State;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// One artifact and its version chain.
/// </summary>
public partial class UC_ArtifactItem : UserControl
{
    private readonly CompositeDisposable subscriptions = [];

    /// <summary>Creates a row for one artifact.</summary>
    public UC_ArtifactItem(string key)
    {
        Key = key;

        InitializeComponent();

        subscriptions.Add(StateStore<MainState>.Default
            .Bind(state => state.ActiveRun.Artifacts.TryGetValue(Key, out ArtifactNode? artifact) ? artifact : null)
            .Subscribe(Show));

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Gets the artifact this row shows.</summary>
    public string Key { get; }

    private void Show(ArtifactNode? artifact)
    {
        if (artifact is null)
            return;

        tbKey.Text = artifact.Key;
        tbState.Text = string.IsNullOrWhiteSpace(artifact.State)
            ? artifact.DisplayText
            : $"{artifact.State} · {artifact.DisplayText}";

        ToolTip = string.IsNullOrWhiteSpace(artifact.SchemaKey) ? artifact.DisplayText : artifact.SchemaKey;

        ArtifactIcon icon = ArtifactIcons.For(artifact.SchemaKey);
        pIcon.Data = Geometry.Parse(icon.Glyph);
        pIcon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(icon.Colour));

        ShowVersions(artifact);
    }

    private void ShowVersions(ArtifactNode artifact)
    {
        wpVersions.Children.Clear();

        for (int index = 0; index < artifact.Versions.Count; index++)
        {
            if (index > 0)
            {
                wpVersions.Children.Add(new TextBlock
                {
                    Text = "→",
                    Foreground = (Brush)FindResource("TextFaint"),
                    FontSize = 9,
                    Margin = new Thickness(3, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            wpVersions.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(2),
                Background = (Brush)FindResource("SurfaceCard"),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 0, 2),

                // The identifier is the tooltip rather than the label: version identifiers are long
                // and the useful thing at a glance is how many there were and which came last.
                ToolTip = artifact.Versions[index],
                Child = new TextBlock
                {
                    Text = "v" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 9
                }
            });
        }
    }
}
