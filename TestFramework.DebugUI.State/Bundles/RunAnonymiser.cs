using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TestFramework.DebugUI.State.Bundles;

/// <summary>
/// Takes the sender out of a run without taking the run apart.
/// </summary>
/// <remarks>
/// <para>
/// Identity in a journal is not smeared through its text. It sits in a handful of named fields, all of them
/// absolute paths, plus the machine name in the sidecar. So this rewrites <em>those fields</em> and nothing
/// else. It is deliberately not a search and replace over the document: that is what would reach into log
/// messages and recorded values, which are the evidence and are not ours to edit.
/// </para>
/// <para>
/// Paths lose their head and keep their tail. The part that identifies someone is the profile directory; the
/// part that is useful is the tail, and it is the tail that lets the recipient find the same project on their
/// own machine. Redacting therefore costs no function: an anonymous run still re-runs and still opens in an
/// editor.
/// </para>
/// </remarks>
public static class RunAnonymiser
{
    /// <summary>What replaces a user profile directory.</summary>
    public const string UserPlaceholder = "<user>";

    /// <summary>What replaces a machine name inside a share path.</summary>
    public const string MachinePlaceholder = "<machine>";

    /// <summary>
    /// The fields that hold an absolute path, and are therefore rewritten.
    /// </summary>
    /// <remarks>
    /// Enumerated rather than guessed at, so what is touched can be read off a list and checked. Every one of
    /// these is a path the framework recorded about where it was running, never something a test produced.
    /// </remarks>
    public static readonly ImmutableHashSet<string> PathFields = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Path",
        "AssemblyPath",
        "ProjectPath",
        "ProjectFilePath",
        "ProjectDisplayName",
        "SourceFilePath");

    /// <summary>The fields dropped outright, because there is no useful tail to keep.</summary>
    public static readonly ImmutableHashSet<string> DroppedFields = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "MachineName");

    /// <summary>
    /// Rewrites the identifying fields of one JSON document in place, reporting how many it changed.
    /// </summary>
    public static int Redact(JToken token, Identity identity)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(identity);

        int changed = 0;

        foreach (JProperty property in Properties(token).ToList())
        {
            if (DroppedFields.Contains(property.Name))
            {
                if (property.Value.Type != JTokenType.Null)
                {
                    property.Value = JValue.CreateNull();
                    changed++;
                }

                continue;
            }

            if (!PathFields.Contains(property.Name) || property.Value.Type != JTokenType.String)
                continue;

            string before = property.Value.Value<string>() ?? string.Empty;
            string after = RedactPath(before, identity);

            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                property.Value = after;
                changed++;
            }
        }

        return changed;
    }

    /// <summary>
    /// Replaces the identifying head of a path, keeping everything that makes it findable.
    /// </summary>
    public static string RedactPath(string? path, Identity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(path))
            return path ?? string.Empty;

        string result = path;

        // A share is named after the machine that serves it, so the head of one is identifying too.
        if (!string.IsNullOrWhiteSpace(identity.MachineName))
        {
            string share = @"\\" + identity.MachineName;

            if (result.StartsWith(share, StringComparison.OrdinalIgnoreCase))
                result = @"\\" + MachinePlaceholder + result[share.Length..];
        }

        if (!string.IsNullOrWhiteSpace(identity.UserProfilePath)
            && result.StartsWith(identity.UserProfilePath, StringComparison.OrdinalIgnoreCase))
        {
            result = UserPlaceholder + result[identity.UserProfilePath.Length..];
        }

        return result;
    }

    /// <summary>
    /// Looks for the sender in the parts that are not ours to rewrite, and says where it is.
    /// </summary>
    /// <remarks>
    /// A label promising more than it delivers would undermine the whole feature, so what cannot be redacted
    /// is at least reported. Log messages, recorded values and artifact file names are left exactly as they
    /// were; if the sender's name is in one of them they are told which field and how often, and the decision
    /// stays theirs.
    /// </remarks>
    public static ImmutableList<AnonymityWarning> Scan(JToken token, Identity identity)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(identity);

        Dictionary<string, int> hits = [];

        foreach (JProperty property in Properties(token))
        {
            // The path fields are rewritten, so a hit in one of them is not a leak.
            if (PathFields.Contains(property.Name) || DroppedFields.Contains(property.Name))
                continue;

            if (property.Value.Type != JTokenType.String)
                continue;

            if (Mentions(property.Value.Value<string>(), identity))
                hits[property.Name] = hits.TryGetValue(property.Name, out int count) ? count + 1 : 1;
        }

        return
        [
            .. hits
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new AnonymityWarning { Field = pair.Key, Occurrences = pair.Value })
        ];
    }

    /// <summary>Whether a piece of content names the sender.</summary>
    public static bool Mentions(string? text, Identity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrEmpty(text))
            return false;

        foreach (string term in identity.Terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Every property in a document, however deeply nested.
    /// </summary>
    /// <remarks>
    /// Only a container has descendants; a document that is a bare value has no fields to rewrite and no
    /// content to search, so it yields nothing rather than being a special case at each call site.
    /// </remarks>
    private static IEnumerable<JProperty> Properties(JToken token)
        => token is JContainer container ? container.Descendants().OfType<JProperty>() : [];

    /// <summary>
    /// Who the sender is, in the terms that appear on disk.
    /// </summary>
    /// <remarks>
    /// Taken as a value rather than read from the environment inside, so the behaviour can be tested without
    /// depending on whose machine the tests happen to run on.
    /// </remarks>
    public sealed record Identity
    {
        /// <summary>Gets the user's profile directory, whose prefix is stripped from paths.</summary>
        public string? UserProfilePath { get; init; }

        /// <summary>Gets the user's account name, searched for in the content that is left alone.</summary>
        public string? UserName { get; init; }

        /// <summary>Gets the machine's name.</summary>
        public string? MachineName { get; init; }

        /// <summary>
        /// Gets the strings whose presence in content counts as naming the sender.
        /// </summary>
        /// <remarks>
        /// The account name and the machine name, not the profile path: a path inside a log line is caught by
        /// the account name within it, and matching the whole path as well would report one line twice.
        /// </remarks>
        public ImmutableList<string> Terms =>
        [
            .. new[] { UserName, MachineName }
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term!)
        ];

        /// <summary>The sender as this machine reports them.</summary>
        public static Identity Current => new()
        {
            UserProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UserName = Environment.UserName,
            MachineName = Environment.MachineName
        };
    }
}

/// <summary>Somewhere the sender is named in content that was not rewritten.</summary>
public sealed record AnonymityWarning
{
    /// <summary>Gets the JSON field it was found in, such as <c>Message</c> or <c>RelativePath</c>.</summary>
    public required string Field { get; init; }

    /// <summary>Gets how many values of that field mention the sender.</summary>
    public required int Occurrences { get; init; }
}
