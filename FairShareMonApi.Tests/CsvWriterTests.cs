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

    // ---- CSV formula-injection hardening (numeric-safe guard, OQ1a/OQ2a) ---------------------------

    [Fact]
    public void EscapeField_LeadingEquals_IsGuardedWithQuotePrefix()
    {
        // A classic formula trigger: the '=' is neutralized by a leading single-quote.
        Assert.Equal("'=SUM(A1)", CsvWriter.EscapeField("=SUM(A1)"));
    }

    [Fact]
    public void EscapeField_LeadingAt_IsGuardedWithQuotePrefix()
    {
        Assert.Equal("'@cmd", CsvWriter.EscapeField("@cmd"));
    }

    [Fact]
    public void EscapeField_LeadingTab_IsGuardedWithQuotePrefix()
    {
        // TAB (0x09) is always guarded; it is not a quote-forcing char so no RFC-4180 wrapping.
        Assert.Equal("'\tx", CsvWriter.EscapeField("\tx"));
    }

    [Fact]
    public void EscapeField_LeadingCarriageReturn_IsGuardedAndQuoted()
    {
        // CR is always guarded (prefix '), and because the value then contains CR it is also quoted.
        Assert.Equal("\"'\rx\"", CsvWriter.EscapeField("\rx"));
    }

    [Fact]
    public void EscapeField_LeadingMinusText_IsGuarded()
    {
        // '-' followed by non-numeric text is a formula, so it is guarded.
        Assert.Equal("'-cmd", CsvWriter.EscapeField("-cmd"));
    }

    [Fact]
    public void EscapeField_LeadingPlusText_IsGuarded()
    {
        Assert.Equal("'+cmd", CsvWriter.EscapeField("+cmd"));
    }

    [Theory]
    [InlineData("-1+2)", "'-1+2)")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("=cmd|'/C calc'!A0", "'=cmd|'/C calc'!A0")]
    public void EscapeField_FormulaExpressionText_IsGuarded(string input, string expected)
    {
        Assert.Equal(expected, CsvWriter.EscapeField(input));
    }

    [Fact]
    public void EscapeField_GuardedFieldWithComma_IsGuardedThenQuoted()
    {
        // Guard runs before RFC-4180 quoting: '=1,2' → '=1,2 (guarded) → wrapped because of the comma.
        Assert.Equal("\"'=1,2\"", CsvWriter.EscapeField("=1,2"));
    }

    [Fact]
    public void EscapeField_GuardedFieldWithQuote_IsGuardedQuotedAndDoubled()
    {
        // '=a"b' → guarded to '=a"b, then quoted with the embedded quote doubled.
        Assert.Equal("\"'=a\"\"b\"", CsvWriter.EscapeField("=a\"b"));
    }

    [Theory]
    [InlineData("-500000.00")] // the load-bearing negative-balance money case (OQ5)
    [InlineData("0.00")]
    [InlineData("800000.00")]
    [InlineData("500000.00")]
    [InlineData("300000.00")]
    [InlineData("-0.01")]
    [InlineData("123.45")]
    [InlineData("266.67")]
    public void EscapeField_MoneyShapedDecimal_IsNotGuarded(string money)
    {
        // Numeric-safe heuristic: money-shaped invariant decimals (incl. negatives) stay byte-identical.
        Assert.Equal(money, CsvWriter.EscapeField(money));
    }

    [Theory]
    [InlineData("14/07/2026")]
    [InlineData("02/03/2026 01:30")]
    public void EscapeField_DateText_IsNotGuarded(string date)
    {
        // Dates start with a digit → not a formula trigger → untouched.
        Assert.Equal(date, CsvWriter.EscapeField(date));
    }

    [Fact]
    public void EscapeField_NumberLikeText_IsNotGuarded_AcceptedHeuristicGap()
    {
        // Documented, accepted trade-off (OQ1a): a text value that parses as a bare number (e.g. a phone
        // number note) is NOT guarded — harmless, Excel just shows the number, no data exfiltration.
        Assert.Equal("+84901234567", CsvWriter.EscapeField("+84901234567"));
    }

    [Theory]
    [InlineData("Tôi")]
    [InlineData("Bình")]
    [InlineData("Bình (đã xóa)")]
    [InlineData("Ăn tối: chia đều")]
    public void EscapeField_NonDangerousText_IsUnchanged(string text)
    {
        Assert.Equal(text, CsvWriter.EscapeField(text));
    }

    [Fact]
    public void EscapeField_EmptyString_IsUnchanged()
    {
        Assert.Equal(string.Empty, CsvWriter.EscapeField(string.Empty));
    }

    [Theory]
    [InlineData("a=b")]
    [InlineData("a+b")]
    [InlineData("a-b")]
    [InlineData("a@b")]
    public void EscapeField_DangerousCharNotInFirstPosition_IsUnchanged(string text)
    {
        // The trigger must be the FIRST char; an interior '='/'@'/'+'/'-' is harmless and left alone.
        Assert.Equal(text, CsvWriter.EscapeField(text));
    }

    [Fact]
    public void FormatRow_NeutralizesFormulaCellsButLeavesMoneyRaw()
    {
        // Guard flows through the FormatRow choke point every cell already passes through.
        var row = CsvWriter.FormatRow(["=cmd", "-500000.00", "Bình"]);

        Assert.Equal("'=cmd,-500000.00,Bình", row);
    }
}
