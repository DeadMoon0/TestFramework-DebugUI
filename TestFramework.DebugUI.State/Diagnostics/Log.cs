using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace TestFramework.DebugUI.State.Diagnostics;

/// <summary>
/// Where the tool writes what went wrong when nobody is watching.
/// </summary>
/// <remarks>
/// <para>
/// Anything a user needs to act on goes to the message feed, and this is for everything else: the
/// failures a surface deliberately swallows because they must not stop the tool. Those were written
/// with <see cref="Debug.WriteLine(object)"/>, which compiles away in a release build — so in the
/// build people actually run, the tool that exists to explain other software's failures kept no
/// record of its own.
/// </para>
/// <para>
/// <b>Nothing here throws at the caller.</b> A log is a convenience, and reporting that reporting
/// failed helps nobody: every failure ends here, including running out of disk to complain about
/// running out of disk.
/// </para>
/// </remarks>
public static class Log
{
    /// <summary>
    /// How large the log may grow before it is rolled over.
    /// </summary>
    /// <remarks>
    /// Watch mode leaves the tool running for days, so this is a bound rather than a hope. One
    /// previous file is kept: enough to still hold the failure that came just before a restart,
    /// without accumulating a directory of them.
    /// </remarks>
    private const long MaximumBytes = 1024 * 1024;

    private const string FolderName = "TestFramework";
    private const string ToolFolderName = "DebugUI";
    private const string FileName = "debugui.log";

    private static readonly object Gate = new();

    private static string? overriddenPath;

    /// <summary>Gets the file being written to.</summary>
    public static string FilePath => overriddenPath ?? DefaultPath;

    /// <summary>Gets where the log lives unless told otherwise.</summary>
    /// <remarks>
    /// Beside the settings and the run journal, for the reason the settings give: the launcher keeps
    /// several versions side by side and replaces them wholesale, so anything written into a
    /// version's own folder is gone at the next update.
    /// </remarks>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName,
        ToolFolderName,
        FileName);

    /// <summary>
    /// Sends the log somewhere else, which is what the tests use.
    /// </summary>
    /// <param name="path">The file to write to, or null to go back to the default.</param>
    public static void WriteTo(string? path)
    {
        lock (Gate)
            overriddenPath = path;
    }

    /// <summary>
    /// Records something that went wrong and was handled.
    /// </summary>
    /// <param name="message">What happened, in the words of whoever handled it.</param>
    public static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        Debug.WriteLine(message);
        Append(message);
    }

    /// <summary>
    /// Records a handled failure.
    /// </summary>
    /// <param name="failure">The failure itself, whose stack names where it was handled.</param>
    public static void Write(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        Write(failure.ToString());
    }

    /// <summary>
    /// Records a handled failure, naming what was being attempted.
    /// </summary>
    /// <param name="doing">What was being attempted, as a phrase: "reading the settings".</param>
    /// <param name="failure">The failure itself.</param>
    public static void Write(string doing, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        Write($"{doing} failed: {failure}");
    }

    private static void Append(string message)
    {
        try
        {
            lock (Gate)
            {
                string path = FilePath;
                string? directory = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                Roll(path);

                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // The last place a failure can be reported is not the place to start raising them.
        }
    }

    private static void Roll(string path)
    {
        FileInfo file = new(path);

        if (!file.Exists || file.Length < MaximumBytes)
            return;

        File.Move(path, path + ".1", overwrite: true);
    }
}
