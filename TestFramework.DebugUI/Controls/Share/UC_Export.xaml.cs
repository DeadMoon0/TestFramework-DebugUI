using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TestFramework.DebugUI.State.Bundles;
using TestFramework.DebugUI.State.Shell.Feed;

namespace TestFramework.DebugUI.Controls.Share;

/// <summary>
/// Asks what should travel with a run before it leaves the machine.
/// </summary>
/// <remarks>
/// Two questions, because there are two: whether the artifact files go too, and whether the sender is redacted.
/// Everything else is either the save dialog's job or the feed's.
/// </remarks>
public partial class UC_Export : UserControl
{
    private ImmutableList<string> journals = [];
    private string? suggestedName;

    /// <summary>Creates the panel.</summary>
    public UC_Export()
    {
        InitializeComponent();

        tgArtifacts.Label = "Include artifacts";
        tgArtifacts.Note = "The files values were written to. Without them the timeline arrives with dead references.";
        tgArtifacts.SetQuietly(true);

        tgAnonymous.Label = "Anonymous";
        tgAnonymous.Note = "Drops your name, machine and user folders from what the framework recorded. Logs, values and artifacts are left as they are.";
        tgAnonymous.SetQuietly(false);

        tgAnonymous.Toggled += _ => ShowWarnings([]);
    }

    /// <summary>Raised when the reader closes the panel without exporting.</summary>
    public event Action? Closed;

    /// <summary>Raised when a bundle has been written.</summary>
    public event Action<RunBundleWriter.Result>? Exported;

    /// <summary>Offers to export the given runs.</summary>
    public void Show(ImmutableList<string> journalPaths, string subject, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(journalPaths);

        journals = journalPaths;
        suggestedName = fileName;

        tbSubject.Text = subject;
        ShowWarnings([]);

        btExport.IsEnabled = journalPaths.Count > 0;
        Visibility = Visibility.Visible;
    }

    private void ShowWarnings(ImmutableList<AnonymityWarning> warnings)
    {
        bWarning.Visibility = warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        tbWarning.Text = warnings.Count == 0
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                warnings.Select(warning => $"{warning.Field} — {warning.Occurrences} value(s) mention you"));
    }

    private async void btExport_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Filter = BundleFormat.DialogFilter,
            DefaultExt = BundleFormat.Extension,
            AddExtension = true,
            FileName = suggestedName ?? BundleFormat.SuggestedFileName(null, journals.Count, DateTimeOffset.Now),
            Title = "Share runs"
        };

        if (dialog.ShowDialog() != true)
            return;

        RunBundleWriter.Request request = new()
        {
            JournalPaths = journals,
            IncludeArtifacts = tgArtifacts.IsOn,
            Anonymous = tgAnonymous.IsOn,
            ExportedBy = Environment.UserName,
            ToolVersion = typeof(UC_Export).Assembly.GetName().Version?.ToString(),
            Identity = RunAnonymiser.Identity.Current,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        btExport.IsEnabled = false;

        try
        {
            // Off the UI thread: a run with a hundred megabytes of artifacts would otherwise freeze the window
            // for as long as the zip takes, and the window is what is showing the progress.
            RunBundleWriter.Result result = await Task.Run(() => RunBundleWriter.Write(dialog.FileName, request));

            Exported?.Invoke(result);

            // The panel stays open when there is something the reader ought to see about their own anonymity,
            // and gets out of the way when there is not.
            if (result.Warnings.Count > 0)
            {
                ShowWarnings(result.Warnings);
                return;
            }

            Closed?.Invoke();
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            MainWindow.Shell.Report(new FeedEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Severity = FeedSeverity.Error,
                Source = FeedSource.App,
                Title = "The runs could not be shared.",
                Detail = exception.Message
            });

            Closed?.Invoke();
        }
        finally
        {
            btExport.IsEnabled = true;
        }
    }

    private void btClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
}
