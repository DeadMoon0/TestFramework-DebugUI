using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using TestFramework.Core.Debugger;

namespace TestFramework.DebugUI.State;

/// <summary>
/// What an inspector shows about one value, worked out apart from the controls that show it.
/// </summary>
/// <remarks>
/// <para>
/// Pure functions from a description to the text on screen. They live here rather than in the WPF
/// control for one reason: the rules are worth testing and a control is not reachable from a test
/// project. The rules that matter are the honest ones — whether a value was cut, how much of it
/// arrived, whether there is more to fetch — and those are exactly the ones that fail quietly.
/// </para>
/// <para>
/// Nothing here decides layout. It decides what is <em>said</em>.
/// </para>
/// </remarks>
public static class ValueInspection
{
    /// <summary>
    /// The one line at the top: the summary, or the older display text when there is no summary.
    /// </summary>
    /// <remarks>
    /// A value replayed from a recording made before values described themselves has a display text
    /// and nothing else. Falling back to it is the difference between an old run reading normally and
    /// reading as a blank panel.
    /// </remarks>
    public static string Headline(ValueDescription described, string displayText)
    {
        ArgumentNullException.ThrowIfNull(described);

        return string.IsNullOrWhiteSpace(described.Summary) ? displayText : described.Summary;
    }

    /// <summary>
    /// The facts to lay out, in the order they are worth reading.
    /// </summary>
    /// <remarks>
    /// The producer's facts lead, then its badges — a lifecycle state is a fact about the value like
    /// any other once there is room to name it — and the schema key last. The schema is not something
    /// the producer says about the value, but it is what a reader needs when asking why the value is
    /// drawn the way it is.
    /// </remarks>
    public static ImmutableList<ValueFact> FactsOf(ValueDescription described, string schemaKey)
    {
        ArgumentNullException.ThrowIfNull(described);

        ImmutableList<ValueFact>.Builder facts = ImmutableList.CreateBuilder<ValueFact>();

        foreach (ValueFact fact in described.Facts)
        {
            // Restated rather than shown bare: a fact cut to fit and a fact that happens to end in an
            // ellipsis look identical, and only one of them has more behind it.
            facts.Add(fact.IsTruncated ? fact with { Value = fact.Value + " (cut)" } : fact);
        }

        foreach (string badge in described.Badges)
            facts.Add(new ValueFact { Name = "state", Value = badge });

        if (!string.IsNullOrWhiteSpace(schemaKey))
            facts.Add(new ValueFact { Name = "schema", Value = schemaKey });

        return facts.ToImmutable();
    }

    /// <summary>
    /// The heading over the content: what form it is in, and how much of it arrived.
    /// </summary>
    /// <remarks>
    /// The second half is the point. A viewer that silently shows four thousand characters of an
    /// eighty-thousand-character value reads as the whole value, which is the misunderstanding the
    /// truncation flag exists to prevent.
    /// </remarks>
    public static string PreviewHeading(ValuePreview? preview, IFormatProvider? culture = null)
    {
        if (preview is null)
            return "NO CONTENT TO SHOW";

        string form = preview.Form switch
        {
            DebugPreviewForm.Json => "JSON",
            DebugPreviewForm.Binary => "BYTES (HEX)",
            _ => "TEXT"
        };

        if (!preview.IsTruncated || preview.SizeInBytes is not { } size)
            return form;

        return $"{form}  —  showing {Size(ShownBy(preview), culture)} of {Size(size, culture)}";
    }

    /// <summary>
    /// The content as it should be read: JSON laid out, everything else as it arrived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It travels minified, because the wire should not carry indentation and because the same
    /// serialised form is what the producer hashes to detect a change. Laying it out is the reader's
    /// job, and only the reader knows there is a window to lay it out in.
    /// </para>
    /// <para>
    /// A cut preview is not valid JSON — it stops mid-array or mid-string — so this formats as far as
    /// the content parses and keeps the remainder verbatim. Refusing to format anything that does not
    /// parse whole would leave exactly the large values unformatted, which are the ones worth
    /// opening an inspector for.
    /// </para>
    /// </remarks>
    public static string PreviewText(ValuePreview? preview)
    {
        if (preview is null)
            return string.Empty;

        return preview.Form switch
        {
            DebugPreviewForm.Json => Indent(preview.Text),
            DebugPreviewForm.Binary => Dump(preview.Text),
            _ => preview.Text
        };
    }

    /// <summary>How many bytes a row of the dump holds.</summary>
    private const int DumpBytesPerRow = 16;

    /// <summary>
    /// Bytes as a hex dump: offset, the bytes, and what they say as text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They arrive as one unbroken hex string, which is unreadable at any length — there is no way
    /// to count to the twelfth byte by eye, and no way to see that the first four spell PNG. The
    /// layout every tool that shows bytes uses solves both, so this uses it.
    /// </para>
    /// <para>
    /// The text gutter is the half people actually read. Most binary values in a test run are only
    /// nominally binary — a UTF-8 body, a header block — and the gutter is what makes that obvious
    /// instead of leaving someone to decode hex pairs.
    /// </para>
    /// </remarks>
    private static string Dump(string hex)
    {
        if (hex.Length == 0)
            return hex;

        // A dangling half byte is dropped: it carries nothing a reader can use, and the heading
        // already counts whole bytes, so keeping it would make the dump disagree with the count.
        byte[]? bytes = TryReadBytes(hex);

        if (bytes is null)
            return hex;

        List<string> rows = [];

        for (int offset = 0; offset < bytes.Length; offset += DumpBytesPerRow)
            rows.Add(Row(bytes, offset, Math.Min(DumpBytesPerRow, bytes.Length - offset)));

        return string.Join(Environment.NewLine, rows);
    }

    private static string Row(byte[] bytes, int offset, int count)
    {
        StringBuilder row = new();

        row.Append(offset.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");

        for (int index = 0; index < DumpBytesPerRow; index++)
        {
            // A short last row is padded rather than left ragged, so the text gutter stays in the
            // same column and the dump can still be read down.
            row.Append(index < count ? bytes[offset + index].ToString("X2", CultureInfo.InvariantCulture) : "  ").Append(' ');

            if (index == (DumpBytesPerRow / 2) - 1)
                row.Append(' ');
        }

        row.Append(' ').Append('|');

        for (int index = 0; index < count; index++)
        {
            char character = (char)bytes[offset + index];

            row.Append(character is >= ' ' and <= '~' ? character : '.');
        }

        return row.Append('|').ToString();
    }

    /// <summary>Reads whole bytes from a hex string, or nothing at all when it is not hex.</summary>
    /// <remarks>
    /// Content that does not decode is shown exactly as it came. A viewer that invented a dump from
    /// something it had not understood would be inventing the value.
    /// </remarks>
    private static byte[]? TryReadBytes(string hex)
    {
        byte[] bytes = new byte[hex.Length / 2];

        for (int index = 0; index < bytes.Length; index++)
        {
            int high = Nibble(hex[index * 2]);
            int low = Nibble(hex[(index * 2) + 1]);

            if (high < 0 || low < 0)
                return null;

            bytes[index] = (byte)((high << 4) | low);
        }

        return bytes;
    }

    private static int Nibble(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'A' and <= 'F' => character - 'A' + 10,
        >= 'a' and <= 'f' => character - 'a' + 10,
        _ => -1
    };

    private static string Indent(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        StringWriter output = new(CultureInfo.InvariantCulture);

        // How far the reader got through tokens it understood. Tracked after each one rather than
        // read off the exception: a preview cut in the middle of a string leaves the reader reporting
        // the end of the input, and the half-written string — content the reader was sent — would be
        // dropped on the floor.
        int consumed = 0;

        using (JsonTextWriter writer = new(output) { Formatting = Formatting.Indented, Indentation = 2 })
        using (JsonTextReader reader = new(new StringReader(json)))
        {
            try
            {
                while (reader.Read())
                {
                    writer.WriteToken(reader, writeChildren: false);
                    consumed = reader.LinePosition;
                }
            }
            catch (JsonReaderException)
            {
                // Where the content was cut. The tail is not JSON any more, so it goes out as it came
                // rather than being hidden.
            }

            writer.Flush();
        }

        string remainder = consumed < json.Length ? json[consumed..] : string.Empty;

        string indented = remainder.Length == 0
            ? output.ToString()
            : output + Environment.NewLine + remainder;

        return Collapse(indented);
    }

    /// <summary>How wide a run of collapsed scalars is allowed to get before it wraps.</summary>
    private const int InlineWidth = 88;

    /// <summary>
    /// Puts runs of plain values back onto shared lines.
    /// </summary>
    /// <remarks>
    /// Indenting is for structure. A serializer indents everything, which turns an array of four
    /// thousand numbers into four thousand lines — strictly worse than the minified form it started
    /// as, and worse in exactly the case an inspector is opened for. Objects and nesting keep their
    /// layout, because there the indentation is the information.
    /// </remarks>
    private static string Collapse(string indented)
    {
        string[] lines = indented.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<string> collapsed = [];
        int index = 0;

        while (index < lines.Length)
        {
            if (!IsPlainValue(lines[index]))
            {
                collapsed.Add(lines[index++]);
                continue;
            }

            string indent = IndentOf(lines[index]);
            List<string> run = [];

            // Only values sharing one indentation belong on one line: a change of depth is a change
            // of container, and running two containers' contents together would misreport the shape.
            while (index < lines.Length && IsPlainValue(lines[index]) && IndentOf(lines[index]) == indent)
                run.Add(lines[index++].Trim());

            collapsed.AddRange(Wrap(run, indent));
        }

        return string.Join(Environment.NewLine, collapsed);
    }

    private static IEnumerable<string> Wrap(List<string> run, string indent)
    {
        StringBuilder line = new(indent);

        foreach (string token in run)
        {
            if (line.Length > indent.Length && line.Length + token.Length + 1 > InlineWidth)
            {
                yield return line.ToString();
                line = new StringBuilder(indent);
            }

            if (line.Length > indent.Length)
                line.Append(' ');

            line.Append(token);
        }

        if (line.Length > indent.Length)
            yield return line.ToString();
    }

    /// <summary>
    /// Whether a line is a value on its own, rather than structure or a named member.
    /// </summary>
    /// <remarks>
    /// A named member keeps its own line: the name is what makes it findable, and running twenty of
    /// them together is how a readable object becomes a paragraph.
    /// </remarks>
    private static string IndentOf(string line) => line[..(line.Length - line.TrimStart().Length)];

    private static bool IsPlainValue(string line)
    {
        string trimmed = line.Trim();

        if (trimmed.Length == 0 || trimmed.Contains("\": ", StringComparison.Ordinal))
            return false;

        return trimmed is not ("{" or "}" or "[" or "]" or "}," or "],")
               && !trimmed.EndsWith('{')
               && !trimmed.EndsWith('[');
    }

    /// <summary>
    /// How much of the value the preview actually carries.
    /// </summary>
    /// <remarks>
    /// Hex spends two characters per byte, so counting its characters would claim twice as much
    /// arrived as did — and claim it against a total measured in real bytes.
    /// </remarks>
    /// <summary>
    /// Breaks prose into lines of at most <paramref name="columns"/> characters, on word boundaries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For text shown in a monospaced pane whose document is deliberately far wider than the panel, so
    /// that a line of code is never broken mid-statement. That setting also stops prose wrapping, which
    /// leaves an explanation running off to the right where nobody scrolls to read it.
    /// </para>
    /// <para>
    /// Wrapped by counting characters rather than by measuring, because the pane is monospaced — every
    /// character is the same width, so a count is exact, and it needs no layout pass to have happened
    /// first. Binding a width to the viewport instead produced a zero-width block that rendered
    /// nothing at all until the panel was resized.
    /// </para>
    /// <para>
    /// A single word longer than the budget is emitted on its own over-long line rather than cut: it is
    /// usually a path or a hash, and half of one is worse than a line that overflows.
    /// </para>
    /// </remarks>
    public static ImmutableList<string> WrapToWidth(string? text, int columns)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        if (columns <= 0)
            return [text];

        ImmutableList<string>.Builder lines = ImmutableList.CreateBuilder<string>();
        System.Text.StringBuilder line = new();

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > columns)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');

            line.Append(word);
        }

        if (line.Length > 0)
            lines.Add(line.ToString());

        return lines.ToImmutable();
    }

    public static long ShownBy(ValuePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return preview.Form == DebugPreviewForm.Binary
            ? preview.Text.Length / 2
            : preview.Text.Length;
    }

    /// <summary>
    /// The line naming the file the whole value went to, or nothing when it did not go to one.
    /// </summary>
    /// <remarks>
    /// The path relative to the run, because that is the one that still means something to whoever
    /// reads the results on another machine.
    /// </remarks>
    public static string? BodyLine(ValueBody? body, IFormatProvider? culture = null)
        => body is null ? null : $"{body.RelativePath}  ·  {Size(body.SizeInBytes, culture)}";

    /// <summary>
    /// A byte count at a size a person reads.
    /// </summary>
    /// <param name="bytes">The count.</param>
    /// <param name="culture">
    /// Which culture formats the number. Defaults to the reader's, because this is display text; a
    /// caller passes one explicitly when the result has to be the same everywhere.
    /// </param>
    public static string Size(long bytes, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        return bytes switch
        {
            < 1024 => string.Format(culture, "{0} B", bytes),
            < 1024 * 1024 => string.Format(culture, "{0:0.#} KB", bytes / 1024d),
            _ => string.Format(culture, "{0:0.#} MB", bytes / (1024d * 1024d))
        };
    }
}
