using System;
using System.IO;

namespace TestFramework.DebugUI.State.Bundles;

/// <summary>
/// The shape of a shared run bundle.
/// </summary>
/// <remarks>
/// <para>
/// A zip with its own extension and a manifest inside, which is what <c>.docx</c>, <c>.nupkg</c> and
/// <c>.jar</c> all are. It stays a genuine zip on purpose: renaming it gets the files out with nothing but
/// a file manager, which is the escape hatch anyone will eventually need.
/// </para>
/// <para>
/// Laid out for several runs from the first version, so "here is the one that failed" and "here is the whole
/// suite" are the same format rather than two.
/// </para>
/// </remarks>
public static class BundleFormat
{
    /// <summary>The extension a bundle carries, including the dot.</summary>
    public const string Extension = ".tfrun";

    /// <summary>The filter a file dialog offers.</summary>
    public const string DialogFilter = "Test Framework run (*.tfrun)|*.tfrun";

    /// <summary>
    /// The version of the layout inside.
    /// </summary>
    /// <remarks>
    /// Read by the importer so a bundle written by a later tool is refused with an explanation rather than
    /// half-read. Bumped only when the layout changes in a way an older reader could not survive.
    /// </remarks>
    public const int Version = 1;

    /// <summary>The manifest's entry name.</summary>
    public const string ManifestEntry = "manifest.json";

    /// <summary>The folder each run's own entries live under.</summary>
    public const string RunsFolder = "runs";

    /// <summary>The folder a run's artifact files live under, matching the relative paths in its journal.</summary>
    public const string ValuesFolder = "values";

    /// <summary>
    /// The annotation file's name within a run's folder.
    /// </summary>
    /// <remarks>
    /// Nothing writes this yet. The name is fixed now so that adding notes and drawings later needs no
    /// version bump: a reader that finds no such entry simply has a bundle nobody annotated.
    /// </remarks>
    public const string AnnotationsFile = "annotations.json";

    /// <summary>The entry prefix for one run.</summary>
    public static string FolderFor(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return $"{RunsFolder}/{sessionId}";
    }

    /// <summary>
    /// Where a run's artifacts sit once imported, which is a folder beside its journal named after it.
    /// </summary>
    /// <remarks>
    /// Named from the journal rather than from the session, so resolving a file needs only the journal's own
    /// path — which is the one thing every surface showing a value already has.
    /// </remarks>
    public static string ArtifactFolderFor(string journalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);

        string directory = Path.GetDirectoryName(journalPath) ?? string.Empty;

        return Path.Combine(directory, Path.GetFileNameWithoutExtension(journalPath));
    }

    /// <summary>A file name for a bundle, built from what is in it rather than from where it came from.</summary>
    /// <remarks>
    /// Deliberately not derived from any folder path: the name is on the outside of the envelope, and a
    /// bundle exported anonymously must not be titled after the person who sent it.
    /// </remarks>
    public static string SuggestedFileName(string? runName, int runCount, DateTimeOffset at)
    {
        string stamp = at.ToUniversalTime().ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);

        string subject = runCount == 1 && !string.IsNullOrWhiteSpace(runName)
            ? Safe(runName)
            : $"{runCount}-runs";

        return $"{subject}-{stamp}{Extension}";
    }

    private static string Safe(string name)
    {
        char[] cleaned = name.ToCharArray();

        for (int index = 0; index < cleaned.Length; index++)
        {
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), cleaned[index]) >= 0)
                cleaned[index] = '_';
        }

        return new string(cleaned);
    }
}
