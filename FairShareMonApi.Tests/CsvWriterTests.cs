using FairShareMonApi.Services.Api.Export.Formatters;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for the hand-rolled RFC-4180 <see cref="CsvWriter"/> (M8, OQ2/OQ4). Proves a
/// field is quoted iff it contains the comma delimiter, a double-quote, CR or LF; embedded quotes are
/// doubled; a plain field is left unquoted; a row joins escaped fields with the comma; and the line
/// ending is CRLF.
/// </summary>
public class CsvWriterTests
{
    [Fact]
    public void EscapeField_PlainText_IsNotQuoted()
    {
        Assert.Equal("Bình", CsvWriter.EscapeField("Bình"));
    }

    [Fact]
    public void EscapeField_Null_BecomesEmptyUnquoted()
    {
        Assert.Equal(string.Empty, CsvWriter.EscapeField(null));
    }

    [Fact]
    public void EscapeField_ContainsComma_IsQuoted()
    {
        Assert.Equal("\"a,b\"", CsvWriter.EscapeField("a,b"));
    }

    [Fact]
    public void EscapeField_ContainsQuote_IsQuotedAndDoubled()
    {
        Assert.Equal("\"say \"\"hi\"\"\"", CsvWriter.EscapeField("say \"hi\""));
    }

    [Fact]
    public void EscapeField_ContainsCarriageReturn_IsQuoted()
    {
        Assert.Equal("\"a\rb\"", CsvWriter.EscapeField("a\rb"));
    }

    [Fact]
    public void EscapeField_ContainsLineFeed_IsQuoted()
    {
        Assert.Equal("\"a\nb\"", CsvWriter.EscapeField("a\nb"));
    }

    [Fact]
    public void FormatRow_JoinsEscapedFieldsWithComma()
    {
        var row = CsvWriter.FormatRow(["Bình", "300000.00", "chia đều"]);

        Assert.Equal("Bình,300000.00,chia đều", row);
    }

    [Fact]
    public void FormatRow_QuotesOnlyTheFieldsThatNeedIt()
    {
        var row = CsvWriter.FormatRow(["plain", "has,comma", "has\"quote"]);

        Assert.Equal("plain,\"has,comma\",\"has\"\"quote\"", row);
    }

    [Fact]
    public void LineEnding_IsCrLf()
    {
        Assert.Equal("\r\n", CsvWriter.LineEnding);
    }
}
