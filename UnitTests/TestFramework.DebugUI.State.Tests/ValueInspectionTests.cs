using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers what an inspector says about a value.
/// </summary>
/// <remarks>
/// The rules worth pinning are the honest ones — whether content was cut, how much of it arrived,
/// whether there is more to fetch. Those are the ones that fail quietly: a panel showing four
/// thousand characters of an eighty-thousand-character value looks perfectly correct.
/// </remarks>
public class ValueInspectionTests
{
    [Fact]
    public void ThePreviewHeadingSaysHowMuchArrivedAgainstHowMuchExists()
    {
        string heading = ValueInspection.PreviewHeading(
            Preview(DebugPreviewForm.Text, new string('x', 4_000), truncated: true, size: 82_891),
            CultureInfo.InvariantCulture);

        Assert.Equal("TEXT  —  showing 3.9 KB of 80.9 KB", heading);
    }

    [Fact]
    public void AWholeValueIsNotDescribedAsAPortionOfItself()
    {
        // Nothing was cut, so there is no "showing X of Y" to state and stating one would invent a
        // shortfall that does not exist.
        string heading = ValueInspection.PreviewHeading(
            Preview(DebugPreviewForm.Json, "[1,2,3]", truncated: false, size: 7),
            CultureInfo.InvariantCulture);

        Assert.Equal("JSON", heading);
    }

    [Fact]
    public void HexIsCountedInBytesRatherThanInTheCharactersItTakesToWriteThem()
    {
        // Two characters per byte. Counting characters would claim twice as much arrived as did, and
        // claim it against a total measured in real bytes.
        ValuePreview preview = Preview(DebugPreviewForm.Binary, new string('A', 4_000), truncated: true, size: 50_000);

        Assert.Equal(2_000, ValueInspection.ShownBy(preview));
        Assert.Contains("showing 2 KB of 48.8 KB", ValueInspection.PreviewHeading(preview, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void TextIsCountedByItsCharacters()
    {
        Assert.Equal(4_000, ValueInspection.ShownBy(Preview(DebugPreviewForm.Text, new string('x', 4_000), truncated: true, size: 4_000)));
    }

    [Fact]
    public void AValueWithNothingToPreviewSaysSoRatherThanShowingAnEmptyBox()
    {
        Assert.Equal("NO CONTENT TO SHOW", ValueInspection.PreviewHeading(null));
    }

    [Fact]
    public void TheFactsLeadWithTheProducersOwnAndEndWithTheSchema()
    {
        // The schema is not something the producer says about the value; it is what a reader needs
        // when asking why the value is drawn the way it is. So it is stated, and stated last.
        ImmutableList<ValueFact> facts = ValueInspection.FactsOf(
            new ValueDescription
            {
                Summary = "Cleaned · File [22 bytes]",
                Facts = [new ValueFact { Name = "kind", Value = "File" }],
                Badges = ["Cleaned"]
            },
            "tf.artifact.file");

        Assert.Equal(["kind", "state", "schema"], facts.Select(fact => fact.Name));
        Assert.Equal("Cleaned", facts.Single(fact => fact.Name == "state").Value);
        Assert.Equal("tf.artifact.file", facts.Single(fact => fact.Name == "schema").Value);
    }

    [Fact]
    public void AFactThatWasCutSaysSo()
    {
        // A fact cut to fit and a fact that happens to end in an ellipsis look identical, and only
        // one of them has more behind it.
        ImmutableList<ValueFact> facts = ValueInspection.FactsOf(
            new ValueDescription
            {
                Summary = "row",
                Facts = [new ValueFact { Name = "reference", Value = "a very long reference", IsTruncated = true }]
            },
            "tf.artifact.sql.row");

        Assert.EndsWith(" (cut)", facts[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueWithNoSchemaGetsNoEmptySchemaRow()
    {
        ImmutableList<ValueFact> facts = ValueInspection.FactsOf(new ValueDescription { Summary = "42" }, string.Empty);

        Assert.Empty(facts);
    }

    [Fact]
    public void TheBodyLineNamesThePathThatTravelsAndTheSize()
    {
        // The relative path, because the absolute one names a directory that does not exist on the
        // machine someone reads the results from.
        string line = ValueInspection.BodyLine(
            new ValueBody { Path = @"C:\runs\x\values\orders.json", RelativePath = "values/orders.json", SizeInBytes = 18_894 },
            CultureInfo.InvariantCulture)!;

        Assert.Contains("values/orders.json", line, StringComparison.Ordinal);
        Assert.Contains("18.5 KB", line, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\runs", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueThatWentToNoFileHasNoBodyLine()
    {
        Assert.Null(ValueInspection.BodyLine(null));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(18_894, "18.5 KB")]
    [InlineData(1_048_576, "1 MB")]
    [InlineData(5_452_595, "5.2 MB")]
    public void ASizeIsGivenAtAScaleAPersonReads(long bytes, string expected)
    {
        Assert.Equal(expected, ValueInspection.Size(bytes, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ASizeIsWrittenForWhoeverIsReadingIt()
    {
        // Display text, so the decimal separator is the reader's. Pinning it to one culture in the
        // control would show 18.5 KB to someone whose every other number reads 18,5.
        Assert.Equal("18,5 KB", ValueInspection.Size(18_894, CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("18.5 KB", ValueInspection.Size(18_894, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void JsonIsLaidOutForReading()
    {
        // It travels minified, because the wire should not carry indentation. Laying it out is the
        // reader's job — only the reader has a window to lay it out in.
        string formatted = ValueInspection.PreviewText(
            Preview(DebugPreviewForm.Json, """{"name":"Ada","orders":[1,2]}""", truncated: false, size: 29));

        Assert.Contains("\n", formatted, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Ada\"", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonThatWasCutIsStillLaidOutAsFarAsItGoes()
    {
        // The case that matters. A preview is cut precisely when the value was large, so refusing to
        // format anything that does not parse whole would leave every value worth opening unformatted.
        string formatted = ValueInspection.PreviewText(
            Preview(DebugPreviewForm.Json, """{"name":"Ada","orders":[1,2,3""", truncated: true, size: 90_000));

        Assert.Contains("\"name\": \"Ada\"", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingThatArrivedIsDroppedWhileLayingOutCutJson()
    {
        // The tail past the cut is not JSON any more, but it is content the reader was sent, and
        // silently discarding it would be a worse lie than showing it unformatted.
        string formatted = ValueInspection.PreviewText(
            Preview(DebugPreviewForm.Json, """{"a":1,"b":"unterminat""", truncated: true, size: 90_000));

        Assert.Contains("unterminat", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunOfPlainValuesStaysOnSharedLinesRatherThanOnePerLine()
    {
        // Indenting is for structure. A serializer indents everything, which turns an array of four
        // thousand numbers into four thousand lines — strictly worse than the minified form it
        // arrived as, and worse in exactly the case an inspector gets opened for.
        string formatted = ValueInspection.PreviewText(
            Preview(DebugPreviewForm.Json, "[" + string.Join(",", Enumerable.Range(1, 200)) + "]", truncated: false, size: 800));

        string[] lines = formatted.Split('\n');

        Assert.True(lines.Length < 20, $"200 numbers should not take {lines.Length} lines.");
        Assert.Contains("1, 2, 3", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void NamedMembersKeepTheirOwnLines()
    {
        // The name is what makes a member findable. Running twenty of them together turns a readable
        // object into a paragraph.
        string formatted = ValueInspection.PreviewText(
            Preview(DebugPreviewForm.Json, """{"name":"Ada","city":"London","age":36}""", truncated: false, size: 39));

        Assert.Equal(3, formatted.Split('\n').Count(line => line.Contains("\": ", StringComparison.Ordinal)));
    }

    [Fact]
    public void ValuesAtDifferentDepthsAreNotRunTogether()
    {
        // A change of depth is a change of container. Collapsing across one would misreport the shape
        // of the value, which is the one thing the layout is there to convey.
        string formatted = ValueInspection.PreviewText(
            Preview(DebugPreviewForm.Json, "[[1,2],[3,4]]", truncated: false, size: 13));

        Assert.DoesNotContain("2, 3", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void TextIsShownExactlyAsItArrived()
    {
        // Re-wrapping text would change content the reader is looking at.
        Assert.Equal("line one\r\nline two", ValueInspection.PreviewText(Preview(DebugPreviewForm.Text, "line one\r\nline two", truncated: false, size: 18)));
    }

    [Fact]
    public void BytesAreLaidOutAsADumpWithOffsetsAndText()
    {
        // One unbroken hex string is unreadable at any length: there is no way to count to the
        // twelfth byte by eye, and no way to see that the first four spell PNG.
        string dump = ValueInspection.PreviewText(Preview(DebugPreviewForm.Binary, "89504E470D0A1A0A0000000D49484452", truncated: false, size: 16));

        Assert.StartsWith("00000000  89 50 4E 47", dump, StringComparison.Ordinal);
        Assert.EndsWith("|.PNG........IHDR|", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void ADumpBreaksEverySixteenBytes()
    {
        string dump = ValueInspection.PreviewText(Preview(DebugPreviewForm.Binary, new string('4', 2 * 40), truncated: false, size: 40));

        string[] rows = dump.Split(Environment.NewLine);

        Assert.Equal(3, rows.Length);
        Assert.StartsWith("00000010", rows[1], StringComparison.Ordinal);
        Assert.StartsWith("00000020", rows[2], StringComparison.Ordinal);
    }

    [Fact]
    public void UnprintableBytesShowAsDotsRatherThanAsControlCharacters()
    {
        // Writing the byte itself would put a bell or a backspace into the panel.
        string dump = ValueInspection.PreviewText(Preview(DebugPreviewForm.Binary, "00074148", truncated: false, size: 4));

        Assert.EndsWith("|..AH|", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void AShortLastRowKeepsTheTextGutterInItsColumn()
    {
        // Padded rather than left ragged, so the dump can still be read down.
        string dump = ValueInspection.PreviewText(Preview(DebugPreviewForm.Binary, "4142434445464748494A4B4C4D4E4F5041", truncated: true, size: 900));

        string[] rows = dump.Split(Environment.NewLine);

        Assert.Equal(rows[0].IndexOf('|', StringComparison.Ordinal), rows[1].IndexOf('|', StringComparison.Ordinal));
    }

    [Fact]
    public void ContentThatDoesNotDecodeIsShownExactlyAsItCame()
    {
        // A viewer that invented a dump from something it had not understood would be inventing the
        // value.
        Assert.Equal("not hex at all", ValueInspection.PreviewText(Preview(DebugPreviewForm.Binary, "not hex at all", truncated: false, size: 14)));
    }

    [Fact]
    public void AValueWithNoPreviewShowsNothingRatherThanFailing()
    {
        Assert.Equal(string.Empty, ValueInspection.PreviewText(null));
    }

    private static ValuePreview Preview(DebugPreviewForm form, string text, bool truncated, long size)
        => new() { Form = form, Text = text, IsTruncated = truncated, SizeInBytes = size };
}
