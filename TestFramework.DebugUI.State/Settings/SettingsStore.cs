using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace TestFramework.DebugUI.State.Settings;

/// <summary>
/// Reads and writes <see cref="UiSettings"/> on disk.
/// </summary>
/// <remarks>
/// <para>
/// Beside the run journal rather than in the installed folder, because the launcher keeps several
/// versions side by side and replaces them wholesale: settings written into a version's own directory
/// would be lost by the next update, which is the one moment a user is most likely to notice.
/// </para>
/// <para>
/// <b>Nothing here throws at the caller.</b> A settings file is a convenience, and no convenience is
/// worth a tool that will not start. Every failure resolves to defaults on read and to a reported
/// notice on write.
/// </para>
/// </remarks>
public sealed class SettingsStore
{
    private const string FolderName = "TestFramework";
    private const string ToolFolderName = "DebugUI";
    private const string FileName = "settings.json";

    private readonly string path;
    private readonly Action<string>? report;

    /// <summary>Creates a store over the default location.</summary>
    /// <param name="report">Told about a failure, so it can reach the message feed.</param>
    public SettingsStore(Action<string>? report = null)
        : this(DefaultPath, report)
    {
    }

    /// <summary>Creates a store over an explicit file, which is what the tests use.</summary>
    public SettingsStore(string path, Action<string>? report = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        this.path = path;
        this.report = report;
    }

    /// <summary>Where settings live unless told otherwise.</summary>
    public static string DefaultPath => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        FolderName,
        ToolFolderName,
        FileName);

    /// <summary>The file this store reads and writes.</summary>
    public string FilePath => path;

    /// <summary>
    /// Reads the settings, falling back to defaults for anything missing or unreadable.
    /// </summary>
    /// <remarks>
    /// A corrupt file resolves to defaults rather than to an error dialog, and the corrupt file is
    /// kept rather than deleted — it is the only evidence of what went wrong, and the next save
    /// replaces it anyway.
    /// </remarks>
    public UiSettings Load()
    {
        try
        {
            if (!File.Exists(path))
                return UiSettings.Defaults;

            string json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
                return UiSettings.Defaults;

            return JsonConvert.DeserializeObject<UiSettings>(json) ?? UiSettings.Defaults;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            report?.Invoke($"Settings could not be read, so defaults are in use. {e.Message}");

            return UiSettings.Defaults;
        }
    }

    /// <summary>
    /// Writes the settings, reporting rather than throwing when it cannot.
    /// </summary>
    /// <remarks>
    /// Written to a temporary file and moved into place, so a process killed mid-write leaves either
    /// the old file or the new one and never a half-written one. A truncated JSON file would parse as
    /// corrupt on the next start and silently discard every setting.
    /// </remarks>
    public void Save(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string temporary = path + ".tmp";

        try
        {
            string? directory = System.IO.Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(temporary, JsonConvert.SerializeObject(settings with { Version = UiSettings.CurrentVersion }, Formatting.Indented));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            report?.Invoke($"Settings could not be saved. {e.Message}");

            TryRemove(temporary);
        }
    }

    private static void TryRemove(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch (Exception e)
        {
            // Nothing useful left to do: the write already failed, and a leftover temporary file is
            // harmless next to losing the settings.
            Debug.WriteLine(e);
        }
    }
}
