using System;
using System.Text;
using System.Text.RegularExpressions;

namespace TestFramework.DebugUI.State;

/// <summary>How a written term is meant to be matched.</summary>
public enum SearchPatternKind
{
    /// <summary>Anywhere in the candidate, ignoring case. What plain typing means.</summary>
    Substring,

    /// <summary>Shell-style, where <c>*</c> stands for any run of characters and <c>?</c> for one.</summary>
    Wildcard,

    /// <summary>A regular expression, written between slashes.</summary>
    Regex
}

/// <summary>
/// One written term, and how to tell whether something matches it.
/// </summary>
/// <remarks>
/// <para>
/// A plain class rather than a record because it holds a compiled <see cref="System.Text.RegularExpressions.Regex"/>.
/// Two patterns compiled from the same text are different instances, so generated equality would report them
/// unequal and anything comparing queries would see a change on every keystroke.
/// </para>
/// <para>
/// The three kinds are inferred from what was typed rather than chosen from a dropdown: plain text is a
/// substring, text containing <c>*</c> or <c>?</c> is a wildcard, and text between slashes is a regular
/// expression. Nobody has to learn a mode to search for a word, and the people who want
/// <c>/Wait(ing)?For/</c> can have it.
/// </para>
/// </remarks>
public sealed class SearchPattern
{
    /// <summary>
    /// How long a single match is allowed to take.
    /// </summary>
    /// <remarks>
    /// The expression is written by whoever is typing, so it can be one that backtracks catastrophically. A
    /// bounded match turns that into a term that finds nothing instead of a window that stops responding.
    /// </remarks>
    private static readonly TimeSpan MatchBudget = TimeSpan.FromMilliseconds(100);

    private readonly Regex? expression;

    private SearchPattern(string text, SearchPatternKind kind, Regex? expression)
    {
        Text = text;
        Kind = kind;
        this.expression = expression;
    }

    /// <summary>Gets the term as it was written.</summary>
    public string Text { get; }

    /// <summary>Gets how it is being matched.</summary>
    public SearchPatternKind Kind { get; }

    /// <summary>
    /// Reads a term, or reports why it could not be read.
    /// </summary>
    /// <remarks>
    /// A malformed regular expression is a normal thing to type — it is malformed for every keystroke up to
    /// the last one — so it is answered with a reason rather than an exception.
    /// </remarks>
    public static bool TryParse(string? text, out SearchPattern? pattern, out string? problem)
    {
        pattern = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();

        // Between slashes, and long enough to have something between them: "/" alone is someone who has
        // started typing, not an empty expression.
        if (trimmed.Length > 2 && trimmed[0] == '/' && trimmed[^1] == '/')
        {
            string body = trimmed[1..^1];

            try
            {
                pattern = new SearchPattern(
                    trimmed,
                    SearchPatternKind.Regex,
                    new Regex(body, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchBudget));

                return true;
            }
            catch (ArgumentException e)
            {
                problem = $"{body} is not a valid regular expression: {e.Message}";
                return false;
            }
        }

        if (trimmed.Contains('*', StringComparison.Ordinal) || trimmed.Contains('?', StringComparison.Ordinal))
        {
            pattern = new SearchPattern(
                trimmed,
                SearchPatternKind.Wildcard,
                new Regex(WildcardToExpression(trimmed), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchBudget));

            return true;
        }

        pattern = new SearchPattern(trimmed, SearchPatternKind.Substring, expression: null);
        return true;
    }

    /// <summary>Whether a candidate matches, treating nothing as no match.</summary>
    public bool Matches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
            return false;

        if (expression is null)
            return candidate.Contains(Text, StringComparison.OrdinalIgnoreCase);

        try
        {
            return expression.IsMatch(candidate);
        }
        catch (RegexMatchTimeoutException)
        {
            // An expression that cannot answer in the budget is treated as not matching this candidate. The
            // search stays usable and slow, rather than becoming a window that has to be killed.
            return false;
        }
    }

    /// <summary>Whether any of several candidates matches.</summary>
    public bool MatchesAny(params string?[] candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (string? candidate in candidates)
        {
            if (Matches(candidate))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Turns a wildcard into an expression, escaping everything that is not a wildcard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unanchored, so <c>Wait*Blob</c> finds <c>WaitForBlobToAppear</c> without anyone having to write a
    /// trailing star. A wildcard is how people narrow a substring search, not how they describe a whole
    /// string.
    /// </para>
    /// <para>
    /// Everything else goes through <see cref="System.Text.RegularExpressions.Regex.Escape"/> one character at
    /// a time. Step names and value keys contain dots and brackets, and leaving those live would make
    /// <c>Acme.Orders</c> match text it has nothing to do with.
    /// </para>
    /// </remarks>
    private static string WildcardToExpression(string wildcard)
    {
        StringBuilder expression = new();

        foreach (char character in wildcard)
        {
            expression.Append(character switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(character.ToString())
            });
        }

        return expression.ToString();
    }
}
