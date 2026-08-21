using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TestFramework.DebugUI.State.Annotations;

namespace TestFramework.DebugUI.State.Bundles;

/// <summary>
/// Writes runs into a bundle that can be handed to someone else.
/// </summary>
/// <remarks>
/// The journal goes in as it was recorded, unless the export was asked to be anonymous, in which case its
/// identifying path fields are rewritten and nothing else is. Every file the journal names is collected by the
/// relative path the journal already carries, so the recipient's copy resolves without anybody rewriting
/// references.
/// </remarks>
public static class RunBundleWriter
{
    /// <summary>What to put in a bundle, and what to leave out.</summary>
    public sealed record Request
    {
        /// <summary>Gets the runs to export, as journal paths.</summary>
        public required ImmutableList<string> JournalPaths { get; init; }

        /// <summary>Gets a value indicating whether artifact files travel with the runs.</summary>
        public bool IncludeArtifacts { get; init; } = true;

        /// <summary>
        /// Gets a value indicating whether the marks drawn on the runs travel with them.
        /// </summary>
        /// <remarks>
        /// On by default, because the reason to send somebody a run is usually to say something about it, and the
        /// marks are that something. Sharing the evidence and withholding the argument is the odd choice, not the
        /// other way round.
        /// </remarks>
        public bool IncludeAnnotations { get; init; } = true;

        /// <summary>Gets a value indicating whether the sender is redacted.</summary>
        public bool Anonymous { get; init; }

        /// <summary>Gets who is exporting, recorded unless the export is anonymous.</summary>
        public string? ExportedBy { get; init; }

        /// <summary>Gets the tool version to stamp into the manifest.</summary>
        public string? ToolVersion { get; init; }

        /// <summary>Gets who the sender is, for redacting and for scanning.</summary>
        public RunAnonymiser.Identity Identity { get; init; } = new();

        /// <summary>Gets the moment to stamp the bundle with.</summary>
        public DateTimeOffset CreatedAtUtc { get; init; }
    }

    /// <summary>What was written, and what the reader should know about it.</summary>
    public sealed record Result
    {
        /// <summary>Gets the bundle's path.</summary>
        public required string Path { get; init; }

        /// <summary>Gets the manifest that was written.</summary>
        public required BundleManifest Manifest { get; init; }

        /// <summary>Gets the artifact files that the journals named but that were not on disk.</summary>
        public ImmutableList<string> MissingFiles { get; init; } = [];

        /// <summary>
        /// Gets the places the sender is still named, in content that was deliberately not rewritten.
        /// </summary>
        /// <remarks>
        /// Empty on a normal export, because nothing was promised. On an anonymous one this is the honest part
        /// of the promise: these are the fields redaction does not touch.
        /// </remarks>
        public ImmutableList<AnonymityWarning> Warnings { get; init; } = [];

        /// <summary>Gets the number of runs written.</summary>
        public int RunCount => Manifest.Runs.Count;

        /// <summary>Gets the number of artifact files written.</summary>
        public int FileCount => Manifest.Runs.Sum(run => run.Files.Count(file => !file.WasMissing));

        /// <summary>Gets the number of runs that carried marks.</summary>
        public int AnnotatedCount => Manifest.Runs.Count(run => run.HasAnnotations);
    }

    /// <summary>Writes a bundle, replacing anything already at the path.</summary>
    public static Result Write(string bundlePath, Request request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentNullException.ThrowIfNull(request);

        List<BundleRun> runs = [];
        List<string> missing = [];
        Dictionary<string, int> warnings = [];

        // Written to a temporary file and moved into place, so an export interrupted half way through does not
        // leave something that looks like a bundle where the reader asked for one.
        string temporary = bundlePath + ".partial";

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(bundlePath) is { Length: > 0 } directory
            ? directory
            : ".");

        if (File.Exists(temporary))
            File.Delete(temporary);

        using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write))
        using (ZipArchive archive = new(file, ZipArchiveMode.Create))
        {
            foreach (string journalPath in request.JournalPaths)
            {
                if (AddRun(archive, journalPath, request, missing, warnings) is { } run)
                    runs.Add(run);
            }

            BundleManifest manifest = Manifest(request, [.. runs]);

            WriteText(archive, BundleFormat.ManifestEntry, JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }

        File.Move(temporary, bundlePath, overwrite: true);

        return new Result
        {
            Path = bundlePath,
            Manifest = Manifest(request, [.. runs]),
            MissingFiles = [.. missing],
            Warnings =
            [
                .. warnings
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new AnonymityWarning { Field = pair.Key, Occurrences = pair.Value })
            ]
        };
    }

    private static BundleManifest Manifest(Request request, ImmutableList<BundleRun> runs) => new()
    {
        FormatVersion = BundleFormat.Version,
        ToolVersion = request.ToolVersion,
        CreatedAtUtc = request.CreatedAtUtc,
        ExportedBy = request.Anonymous ? null : request.ExportedBy,
        MachineName = request.Anonymous ? null : request.Identity.MachineName,
        IsAnonymous = request.Anonymous,
        IncludesArtifacts = request.IncludeArtifacts,
        IncludesAnnotations = request.IncludeAnnotations,
        Runs = runs
    };

    private static BundleRun? AddRun(
        ZipArchive archive,
        string journalPath,
        Request request,
        List<string> missing,
        Dictionary<string, int> warnings)
    {
        if (!File.Exists(journalPath))
            return null;

        string metadataPath = MetadataPathFor(journalPath);

        if (!File.Exists(metadataPath))
            return null;

        JObject metadata = (JObject)Parse(File.ReadAllText(metadataPath));

        string? sessionId = metadata.Value<string>("SessionId");

        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        string folder = BundleFormat.FolderFor(sessionId);
        List<ValueReference> references = [];
        // Built only when the journal has to change. An export that is not anonymous copies the file byte for
        // byte instead, because re-serialising does not leave it alone: Newtonsoft reads a timestamp as a date
        // and writes it back in the exporter's own time zone, so every "AtUtc" came out carrying a local offset,
        // drifted again on each hop, and told the recipient which zone the sender was in even on an anonymous
        // export. The journal is the evidence; it is copied, not re-typed.
        StringBuilder? rewritten = request.Anonymous ? new StringBuilder() : null;

        // Line by line, because the journal is one JSON document per line and a run of any size is not worth
        // holding in memory twice.
        foreach (string line in File.ReadLines(journalPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JToken record = Parse(line);

            references.AddRange(ReferencesIn(record));

            // Only when anonymity was asked for. Reporting where someone's own name appears in their own logs,
            // on an export that never promised to hide it, is a warning about nothing.
            if (rewritten is null)
                continue;

            Note(RunAnonymiser.Scan(record, request.Identity), warnings);
            RunAnonymiser.Redact(record, request.Identity);

            rewritten.Append(record.ToString(Formatting.None)).Append('\n');
        }

        string journalFileName = System.IO.Path.GetFileName(journalPath);
        string metadataFileName = System.IO.Path.GetFileName(metadataPath);

        if (rewritten is null)
        {
            archive.CreateEntryFromFile(journalPath, $"{folder}/{journalFileName}");
            archive.CreateEntryFromFile(metadataPath, $"{folder}/{metadataFileName}");
        }
        else
        {
            Note(RunAnonymiser.Scan(metadata, request.Identity), warnings);
            RunAnonymiser.Redact(metadata, request.Identity);

            WriteText(archive, $"{folder}/{journalFileName}", rewritten.ToString());
            WriteText(archive, $"{folder}/{metadataFileName}", metadata.ToString(Formatting.Indented));
        }

        List<BundleFile> files = [];

        if (request.IncludeArtifacts)
        {
            foreach (ValueReference reference in Distinct(references))
            {
                bool present = !string.IsNullOrWhiteSpace(reference.AbsolutePath) && File.Exists(reference.AbsolutePath);

                if (present)
                    archive.CreateEntryFromFile(reference.AbsolutePath!, $"{folder}/{reference.RelativePath}");
                else
                    missing.Add(reference.RelativePath);

                files.Add(new BundleFile
                {
                    RelativePath = reference.RelativePath,
                    SizeInBytes = reference.SizeInBytes,
                    ContentHash = reference.ContentHash,
                    WasMissing = !present
                });
            }
        }

        bool annotated = request.IncludeAnnotations && AddAnnotations(archive, journalPath, folder, sessionId, request);

        return new BundleRun
        {
            HasAnnotations = annotated,
            SessionId = sessionId,
            Name = metadata.Value<string>("Name") ?? sessionId,
            FullyQualifiedName = metadata["Identity"]?.Value<string>("FullyQualifiedName"),
            StartedAtUtc = Moment(metadata, "StartedAtUtc") ?? default,
            FinishedAtUtc = Moment(metadata, "FinishedAtUtc"),
            JournalFileName = journalFileName,
            MetadataFileName = metadataFileName,
            Files = [.. files]
        };
    }


    /// <summary>
    /// Reads JSON without interpreting anything.
    /// </summary>
    /// <remarks>
    /// Dates stay the strings they were written as. The default behaviour parses them into date values, and
    /// writing those back renders them in the current time zone. Nothing here needs a date as a date; it needs
    /// the document back the way it went in.
    /// </remarks>
    private static JToken Parse(string json)
    {
        using JsonTextReader reader = new(new StringReader(json)) { DateParseHandling = DateParseHandling.None };

        return JToken.ReadFrom(reader);
    }

    /// <summary>
    /// Reads a moment out of a sidecar.
    /// </summary>
    /// <remarks>
    /// Converted rather than cast. A date in JSON is parsed as a <see cref="DateTime"/>, and asking for a
    /// <see cref="DateTimeOffset"/> straight out of the token throws on every real sidecar there is.
    /// </remarks>
    private static DateTimeOffset? Moment(JObject metadata, string name)
        => metadata[name] is { Type: not JTokenType.Null } token ? token.ToObject<DateTimeOffset?>() : null;

    /// <summary>
    /// Puts a run's marks in the bundle, and says whether there were any.
    /// </summary>
    /// <remarks>
    /// Read through the store rather than copied as a file, because an anonymous share has to replace the authors
    /// — and that is a change to the marks, not to the run, so it happens on the way out and never on disk.
    /// </remarks>
    private static bool AddAnnotations(ZipArchive archive, string journalPath, string folder, string sessionId, Request request)
    {
        Annotations.RunAnnotations marks = new Annotations.AnnotationStore().Load(journalPath, sessionId);

        if (marks.IsEmpty)
            return false;

        if (request.Anonymous)
            marks = marks.Anonymised();

        WriteText(archive, $"{folder}/{BundleFormat.AnnotationsFile}", JsonConvert.SerializeObject(marks, Formatting.Indented));

        return true;
    }

    /// <summary>The sidecar that belongs to a journal.</summary>
    /// <remarks>
    /// The two share a base name, which is what lets the listing pair them up; the sidecar is what the run
    /// list actually reads, so a bundle without it would arrive invisible.
    /// </remarks>
    public static string MetadataPathFor(string journalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);

        string directory = System.IO.Path.GetDirectoryName(journalPath) ?? string.Empty;

        return System.IO.Path.Combine(directory, System.IO.Path.GetFileNameWithoutExtension(journalPath) + ".meta.json");
    }

    /// <summary>
    /// Every file a journal record refers to.
    /// </summary>
    /// <remarks>
    /// Recognised by shape rather than by position: a value's body is the object carrying both a path and the
    /// relative path it is known by. Walking for that shape means a body recorded somewhere new in the
    /// protocol is still collected, instead of being silently left out of the bundle.
    /// </remarks>
    private static IEnumerable<ValueReference> ReferencesIn(JToken record)
    {
        if (record is not JContainer container)
            yield break;

        foreach (JObject candidate in container.Descendants().OfType<JObject>())
        {
            string? relative = candidate.Value<string>("RelativePath");

            if (string.IsNullOrWhiteSpace(relative))
                continue;

            yield return new ValueReference
            {
                AbsolutePath = candidate.Value<string>("Path"),
                RelativePath = relative.Replace('\\', '/'),
                SizeInBytes = candidate.Value<long?>("SizeInBytes") ?? 0,
                ContentHash = candidate.Value<string>("ContentHash")
            };
        }
    }

    /// <summary>
    /// One entry per relative path.
    /// </summary>
    /// <remarks>
    /// A value written once and then described again on every later version of itself appears many times in a
    /// journal; the file is the same file, and adding it twice would fail on the duplicate entry name.
    /// </remarks>
    private static IEnumerable<ValueReference> Distinct(IEnumerable<ValueReference> references)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (ValueReference reference in references)
        {
            if (seen.Add(reference.RelativePath))
                yield return reference;
        }
    }

    private static void Note(IEnumerable<AnonymityWarning> found, Dictionary<string, int> into)
    {
        foreach (AnonymityWarning warning in found)
        {
            into[warning.Field] = into.TryGetValue(warning.Field, out int count)
                ? count + warning.Occurrences
                : warning.Occurrences;
        }
    }

    private static void WriteText(ZipArchive archive, string entryName, string content)
    {
        using Stream stream = archive.CreateEntry(entryName).Open();
        using StreamWriter writer = new(stream, new UTF8Encoding(false));

        writer.Write(content);
    }

    private sealed record ValueReference
    {
        public string? AbsolutePath { get; init; }

        public required string RelativePath { get; init; }

        public long SizeInBytes { get; init; }

        public string? ContentHash { get; init; }
    }
}
