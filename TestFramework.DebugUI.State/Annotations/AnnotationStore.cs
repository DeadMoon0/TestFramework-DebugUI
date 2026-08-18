using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace TestFramework.DebugUI.State.Annotations;

/// <summary>
/// Keeps a run's marks beside the run, and never gets in the way.
/// </summary>
/// <remarks>
/// <para>
/// A file of its own next to the journal, named after it. Beside rather than inside, because the journal is the
/// framework's record and this is somebody's opinion of it; visible rather than tucked away somewhere private,
/// because a reader who wants to know what has been drawn on a run should be able to find it.
/// </para>
/// <para>
/// Nothing here throws. Losing a drawing is a disappointment; taking the window down while somebody is drawing,
/// because a disk was full or a file was locked, would lose the run they were annotating as well.
/// </para>
/// </remarks>
public sealed class AnnotationStore
{
    private readonly Action<string>? report;

    /// <summary>Creates a store, optionally telling somebody when it cannot do its job.</summary>
    public AnnotationStore(Action<string>? report = null) => this.report = report;

    /// <summary>
    /// Where a run's marks live.
    /// </summary>
    /// <remarks>
    /// Derived from the journal's own path, so a run that arrived in a bundle finds its marks by the same rule as
    /// one recorded here — the importer only has to put the file in the right place.
    /// </remarks>
    public static string PathFor(string journalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);

        string directory = Path.GetDirectoryName(journalPath) ?? string.Empty;

        return Path.Combine(directory, Path.GetFileNameWithoutExtension(journalPath) + ".annotations.json");
    }

    /// <summary>
    /// Reads a run's marks, or an empty set when there are none.
    /// </summary>
    /// <remarks>
    /// A file that cannot be read is treated as no marks rather than as an error worth stopping for. The
    /// alternative is a run that will not open because somebody's drawing is malformed.
    /// </remarks>
    public RunAnnotations Load(string journalPath, string sessionId)
    {
        RunAnnotations empty = new() { SessionId = sessionId };

        if (string.IsNullOrWhiteSpace(journalPath))
            return empty;

        string path = PathFor(journalPath);

        if (!File.Exists(path))
            return empty;

        try
        {
            RunAnnotations? read = JsonConvert.DeserializeObject<RunAnnotations>(File.ReadAllText(path));

            if (read is null)
                return empty;

            // A file from a later version of the tool may hold marks this one would draw wrongly. Shown as
            // nothing rather than as a guess, and said out loud.
            if (read.FormatVersion > RunAnnotations.CurrentFormatVersion)
            {
                report?.Invoke($"The annotations on this run were made by a newer version of the tool (format {read.FormatVersion}).");

                return empty;
            }

            return read with { SessionId = sessionId };
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            report?.Invoke("The annotations on this run could not be read.");

            return empty;
        }
    }

    /// <summary>
    /// Writes a run's marks, and removes the file when there are none left.
    /// </summary>
    /// <remarks>
    /// Written to a temporary file and moved into place, so a crash halfway through leaves the previous drawing
    /// rather than a half-written one. Undoing the last mark deletes the file instead of leaving an empty one
    /// behind, which keeps "has anyone annotated this" answerable by looking.
    /// </remarks>
    public bool Save(string journalPath, RunAnnotations annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        if (string.IsNullOrWhiteSpace(journalPath))
            return false;

        string path = PathFor(journalPath);

        try
        {
            if (annotations.IsEmpty)
            {
                if (File.Exists(path))
                    File.Delete(path);

                return true;
            }

            string temporary = path + ".partial";

            File.WriteAllText(temporary, JsonConvert.SerializeObject(annotations, Formatting.Indented));
            File.Move(temporary, path, overwrite: true);

            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            report?.Invoke("The annotations on this run could not be saved.");

            return false;
        }
    }
}
