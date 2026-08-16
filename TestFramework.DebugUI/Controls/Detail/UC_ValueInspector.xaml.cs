using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Axiom.State;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Controls.Detail;

/// <summary>
/// One value, in full: its facts laid out, and as much of its content as was sent.
/// </summary>
/// <remarks>
/// <para>
/// The rail has room for a line. This has room for the rest — which matters because the preview is
/// the only part of a large value that travels, and until now nothing in the UI could show it at all.
/// </para>
/// <para>
/// Bound to the key rather than handed a snapshot, so a value that changes while the inspector is
/// open changes on screen. A snapshot would quietly become a lie during a live run.
/// </para>
/// </remarks>
public partial class UC_ValueInspector : UserControl
{
    private CompositeDisposable subscriptions = [];
    private ValueBody? body;

    /// <summary>Creates the inspector, closed and bound to nothing.</summary>
    public UC_ValueInspector()
    {
        InitializeComponent();

        Unloaded += (_, _) => subscriptions.Dispose();
    }

    /// <summary>Raised when the reader closes the inspector.</summary>
    public event Action? Closed;

    /// <summary>Shows a value, following it for as long as the inspector stays open.</summary>
    /// <param name="key">The value's identifier.</param>
    /// <param name="isArtifact">Whether the value is an artifact rather than a variable.</param>
    public void Show(string key, bool isArtifact)
    {
        // Rebound rather than added to: opening a second value while the first is on screen must
        // replace it, not leave two subscriptions writing to the same controls.
        subscriptions.Dispose();
        subscriptions = [];

        subscriptions.Add(isArtifact
            ? StateStore<MainState>.Default
                .Bind(state => state.ActiveRun.Artifacts.TryGetValue(key, out ArtifactNode? artifact) ? artifact : null)
                .Subscribe(ShowArtifact)
            : StateStore<MainState>.Default
                .Bind(state => state.ActiveRun.Variables.TryGetValue(key, out ValueNode? value) ? value : null)
                .Subscribe(ShowVariable));

        Visibility = Visibility.Visible;
    }

    private void ShowVariable(ValueNode? value)
    {
        if (value is null)
            return;

        Render(value.Key, value.SchemaKey, value.DisplayText, value.Description, []);
    }

    private void ShowArtifact(ArtifactNode? artifact)
    {
        if (artifact is null)
            return;

        Render(artifact.Key, artifact.SchemaKey, artifact.DisplayText, artifact.Description, artifact.Versions);
    }

    private void Render(string key, string schemaKey, string displayText, ValueDescription described, ImmutableList<string> versions)
    {
        tbKey.Text = key;
        tbSummary.Text = ValueInspection.Headline(described, displayText);

        ValueIcon icon = ValueIcons.For(schemaKey);
        pIcon.Data = Geometry.Parse(icon.Glyph);
        pIcon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(icon.Colour));

        RenderFacts(described, schemaKey);
        RenderVersions(versions);
        RenderPreview(described);
        RenderBody(described);
    }

    /// <summary>The facts as rows, name beside value.</summary>
    private void RenderFacts(ValueDescription described, string schemaKey)
    {
        gFacts.Children.Clear();
        gFacts.RowDefinitions.Clear();

        foreach (ValueFact fact in ValueInspection.FactsOf(described, schemaKey))
            AddFact(fact.Name, fact.Value);
    }

    private void AddFact(string name, string value)
    {
        int row = gFacts.RowDefinitions.Count;
        gFacts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock label = new()
        {
            Text = name,
            Foreground = (Brush)FindResource("TextFaint"),
            FontSize = 11,
            Margin = new Thickness(0, 1, 14, 1)
        };

        TextBlock body = new()
        {
            Text = value,
            Foreground = (Brush)FindResource("TextSecondary"),
            FontSize = 11,
            Margin = new Thickness(0, 1, 0, 1),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = value
        };

        Grid.SetRow(label, row);
        Grid.SetRow(body, row);
        Grid.SetColumn(body, 1);

        gFacts.Children.Add(label);
        gFacts.Children.Add(body);
    }

    private void RenderVersions(ImmutableList<string> versions)
    {
        wpVersions.Children.Clear();
        wpVersions.Visibility = versions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        for (int index = 0; index < versions.Count; index++)
        {
            if (index > 0)
            {
                wpVersions.Children.Add(new TextBlock
                {
                    Text = "→",
                    Foreground = (Brush)FindResource("TextFaint"),
                    FontSize = 10,
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            wpVersions.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(2),
                Background = (Brush)FindResource("SurfaceCard"),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 0, 2),
                ToolTip = versions[index],
                Child = new TextBlock
                {
                    Text = "v" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    Foreground = (Brush)FindResource("TextSecondary"),
                    FontSize = 10
                }
            });
        }
    }

    /// <summary>The content itself, headed by an honest statement of how much of it arrived.</summary>
    private void RenderPreview(ValueDescription described)
    {
        tbPreviewLabel.Text = ValueInspection.PreviewHeading(described.Preview);
        tbPreview.Text = ValueInspection.PreviewText(described.Preview);
    }

    private void RenderBody(ValueDescription described)
    {
        body = described.Body;

        gBody.Visibility = body is null ? Visibility.Collapsed : Visibility.Visible;

        if (body is null)
            return;

        tbBody.Text = ValueInspection.BodyLine(body);
        tbBody.ToolTip = body.Path;
        btOpen.IsEnabled = File.Exists(body.Path);
    }

    /// <summary>Opens the file the whole value was written to.</summary>
    /// <remarks>
    /// Handed to the shell rather than rendered here: the point of writing values into the run's own
    /// output is that whatever already understands the format can open them.
    /// </remarks>
    private void btOpen_Click(object sender, RoutedEventArgs e)
    {
        if (body is null || !File.Exists(body.Path))
            return;

        Process.Start(new ProcessStartInfo(body.Path) { UseShellExecute = true })?.Dispose();
    }

    private void btClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
}
