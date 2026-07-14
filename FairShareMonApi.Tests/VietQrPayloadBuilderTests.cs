using FairShareMonApi.Services.Api.Wallet;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the hand-rolled <see cref="VietQrPayloadBuilder"/> (M9 OQ1) - the correctness
/// centre-piece. Covers: CRC-16/CCITT-FALSE against the canonical vector "123456789" -> 0x29B1; a full
/// built payload decoded field-by-field to the expected EMVCo TLV; the trailing 6304 tag included in
/// the CRC; the emitted CRC re-validated with an INDEPENDENT (table-driven) CRC implementation written
/// here (never the production routine checking itself); memo ASCII-folding of Vietnamese + truncation;
/// amount formatting with no grouping; and the static (no-amount) variant.
/// </summary>
public class VietQrPayloadBuilderTests
{
    private const string Bin = "970436";        // Vietcombank NAPAS BIN (sample)
    private const string Account = "0123456789";

    private readonly VietQrPayloadBuilder _builder = new();

    // ---- CRC routine ------------------------------------------------------------------------------

    [Fact]
    public void Crc16Ccitt_CanonicalVector_Returns0x29B1()
    {
        // The published CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF) check value.
        Assert.Equal((ushort)0x29B1, VietQrPayloadBuilder.Crc16Ccitt("123456789"));
    }

    [Fact]
    public void Crc16Ccitt_MatchesIndependentTableDrivenImplementation()
    {
        foreach (var sample in new[] { "", "A", "123456789", "00020101021253037045802VN6304", "Ăn uống" })
            Assert.Equal(IndependentCrc16CcittFalse(sample), VietQrPayloadBuilder.Crc16Ccitt(sample));
    }

    // ---- Full payload TLV structure ---------------------------------------------------------------

    [Fact]
    public void Build_DynamicPayload_DecodesFieldByFieldToExpectedTlv()
    {
        var payload = _builder.Build(Bin, Account, 500_000m, "Com trua");

        var fields = ParseTlv(payload);

        Assert.Equal("01", fields["00"]);   // payload format indicator
        Assert.Equal("12", fields["01"]);   // point of initiation: dynamic (amount present)
        Assert.Equal("704", fields["53"]);  // currency VND
        Assert.Equal("500000", fields["54"]); // amount, no grouping/decimals
        Assert.Equal("VN", fields["58"]);   // country

        // Field 38 = NAPAS merchant account block.
        var merchant = ParseTlv(fields["38"]);
        Assert.Equal("A000000727", merchant["00"]);   // NAPAS GUID
        Assert.Equal("QRIBFTTA", merchant["02"]);     // service code: transfer-to-account

        var beneficiary = ParseTlv(merchant["01"]);
        Assert.Equal(Bin, beneficiary["00"]);         // acquirer BIN
        Assert.Equal(Account, beneficiary["01"]);     // consumer account

        // Field 62 = additional data; sub-tag 08 = memo.
        var additional = ParseTlv(fields["62"]);
        Assert.Equal("Com trua", additional["08"]);

        // Field 63 = CRC, 4 upper-hex chars.
        Assert.Equal(4, fields["63"].Length);
        Assert.Matches("^[0-9A-F]{4}$", fields["63"]);
    }

    [Fact]
    public void Build_EveryTlvElement_HasCorrectDeclaredLength()
    {
        var payload = _builder.Build(Bin, Account, 500_000m, "Com trua");

        // A malformed length would make the walker throw or leave a remainder.
        var fields = ParseTlv(payload);
        Assert.NotEmpty(fields); // parsed clean end-to-end
    }

    // ---- CRC is over the whole string including the 6304 tag --------------------------------------

    [Fact]
    public void Build_CrcCoversTheWholePayloadIncludingThe6304Tag_ReValidatesIndependently()
    {
        var payload = _builder.Build(Bin, Account, 500_000m, "Com trua");

        // Split off the last 4 chars (the CRC value); everything before, INCLUDING "6304", is covered.
        var body = payload[..^4];
        var emittedCrc = payload[^4..];

        Assert.EndsWith("6304", body); // the CRC tag+length prefix is part of the CRC input
        var expected = IndependentCrc16CcittFalse(body).ToString("X4");
        Assert.Equal(expected, emittedCrc);
    }

    [Fact]
    public void Build_TamperedPayload_FailsIndependentCrcCheck()
    {
        var payload = _builder.Build(Bin, Account, 500_000m, "Com trua");

        // Flip a digit in the amount; the stored CRC must no longer match a recomputation.
        var tampered = payload.Replace("500000", "900000");
        var body = tampered[..^4];
        var storedCrc = tampered[^4..];

        Assert.NotEqual(IndependentCrc16CcittFalse(body).ToString("X4"), storedCrc);
    }

    // ---- Amount formatting ------------------------------------------------------------------------

    [Theory]
    [InlineData("500000", 500_000)]
    [InlineData("1000000", 1_000_000)]
    [InlineData("50", 50)]
    public void Build_WholeVndAmount_FormatsAsPlainIntegerNoGrouping(string expected, long amount)
    {
        var payload = _builder.Build(Bin, Account, amount, null);
        Assert.Equal(expected, ParseTlv(payload)["54"]);
    }

    [Fact]
    public void Build_AmountWithCents_FormatsUpToTwoDecimalsWithDotSeparator()
    {
        var payload = _builder.Build(Bin, Account, 1234.5m, null);
        Assert.Equal("1234.5", ParseTlv(payload)["54"]);
    }

    // ---- Point of initiation / static (no-amount) variant -----------------------------------------

    [Fact]
    public void Build_ZeroAmount_IsStaticWithNoAmountFieldButStillValidCrc()
    {
        var payload = _builder.Build(Bin, Account, 0m, "Com trua");

        var fields = ParseTlv(payload);
        Assert.Equal("11", fields["01"]);           // static point of initiation
        Assert.False(fields.ContainsKey("54"));     // no amount field
        // CRC still valid over the whole string.
        Assert.Equal(IndependentCrc16CcittFalse(payload[..^4]).ToString("X4"), payload[^4..]);
    }

    [Fact]
    public void Build_NoMemo_OmitsField62ButStillValidCrc()
    {
        var payload = _builder.Build(Bin, Account, 500_000m, null);

        var fields = ParseTlv(payload);
        Assert.False(fields.ContainsKey("62"));
        Assert.Equal(IndependentCrc16CcittFalse(payload[..^4]).ToString("X4"), payload[^4..]);
    }

    // ---- Memo folding + truncation (OQ9) ----------------------------------------------------------

    [Fact]
    public void Build_VietnameseMemo_IsAsciiFoldedAndTruncatedTo25Chars()
    {
        var payload = _builder.Build(Bin, Account, 500_000m, "Ăn uống tại Đà Nẵng nhé bạn ơi");

        var memo = ParseTlv(ParseTlv(payload)["62"])["08"];

        Assert.Equal("An uong tai Da Nang nhe b", memo); // diacritics stripped, đ->d, cut to 25
        Assert.Equal(25, memo.Length);
        Assert.All(memo, ch => Assert.InRange(ch, ' ', '~')); // pure printable ASCII
    }

    [Fact]
    public void Build_MemoWithDMark_MapsDToLatinD()
    {
        var payload = _builder.Build(Bin, Account, 500_000m, "đồng");
        Assert.Equal("dong", ParseTlv(ParseTlv(payload)["62"])["08"]);
    }

    // ---- Test-local helpers -----------------------------------------------------------------------

    /// <summary>
    /// Walks an EMVCo TLV string (id[2] + length[2] + value[length]) into an id->value map. Throws if a
    /// declared length overruns the string - so a malformed payload fails the test loudly.
    /// </summary>
    private static Dictionary<string, string> ParseTlv(string data)
    {
        var map = new Dictionary<string, string>();
        var i = 0;
        while (i < data.Length)
        {
            Assert.True(i + 4 <= data.Length, "TLV truncated at id/length prefix");
            var id = data.Substring(i, 2);
            var length = int.Parse(data.Substring(i + 2, 2));
            Assert.True(i + 4 + length <= data.Length, $"TLV value for '{id}' overruns the payload");
            map[id] = data.Substring(i + 4, length);
            i += 4 + length;
        }

        return map;
    }

    /// <summary>
    /// An INDEPENDENT CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF, no reflection, no final xor) using a
    /// precomputed 256-entry table - a deliberately different implementation from the production inline
    /// bit-loop, so the two agreeing genuinely cross-checks the production routine.
    /// </summary>
    private static ushort IndependentCrc16CcittFalse(string data)
    {
        var table = new ushort[256];
        for (var n = 0; n < 256; n++)
        {
            var entry = (ushort)(n << 8);
            for (var bit = 0; bit < 8; bit++)
                entry = (ushort)((entry & 0x8000) != 0 ? (entry << 1) ^ 0x1021 : entry << 1);
            table[n] = entry;
        }

        ushort crc = 0xFFFF;
        foreach (var ch in data)
            crc = (ushort)((crc << 8) ^ table[((crc >> 8) ^ (byte)ch) & 0xFF]);

        return crc;
    }
}
