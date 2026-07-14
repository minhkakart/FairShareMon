using System.Globalization;
using System.Text;
using DiDecoration.Attributes;

namespace FairShareMonApi.Services.Api.Wallet;

/// <summary>
/// Hand-rolled VietQR payload builder (OQ1). Assembles the EMVCo-compliant TLV string for a
/// bank-transfer QR: <c>00</c> = payload format "01"; <c>01</c> = point-of-initiation ("12" dynamic
/// when an amount is present, else "11" static); <c>38</c> = the NAPAS merchant-account block (GUID
/// <c>A000000727</c> + acquirer BIN + consumer account + service code <c>QRIBFTTA</c>
/// = transfer-to-account); <c>53</c> = currency "704" (VND); <c>54</c> = amount (only when dynamic);
/// <c>58</c> = country "VN"; <c>62</c> = additional data (memo in sub-tag <c>08</c>, ASCII-folded and
/// truncated per OQ9); <c>63</c> = CRC-16/CCITT-FALSE over the whole string including the <c>6304</c>
/// tag, emitted as 4 upper-hex chars. No payload library - the format is precisely specified and fully
/// unit-testable against a known VietQR sample.
/// </summary>
public interface IVietQrPayloadBuilder
{
    /// <summary>Builds the full VietQR payload string for a receiving account, amount and optional memo.</summary>
    string Build(string bankBin, string accountNumber, decimal amount, string? addInfo);
}

[ScopedService(typeof(IVietQrPayloadBuilder))]
public sealed class VietQrPayloadBuilder : IVietQrPayloadBuilder
{
    /// <summary>NAPAS globally unique identifier for VietQR (EMVCo field 38 sub-tag 00).</summary>
    private const string NapasGuid = "A000000727";

    /// <summary>Service code: inter-bank funds transfer to account (EMVCo field 38 sub-tag 02).</summary>
    private const string ServiceCode = "QRIBFTTA";

    /// <summary>Currency code for VND (ISO 4217 numeric, EMVCo field 53).</summary>
    private const string CurrencyVnd = "704";

    /// <summary>Country code (EMVCo field 58).</summary>
    private const string CountryVn = "VN";

    /// <summary>Max memo length after folding (some bank apps truncate longer memos, OQ9).</summary>
    public const int MemoMaxLength = 25;

    public string Build(string bankBin, string accountNumber, decimal amount, string? addInfo)
    {
        var isDynamic = amount > 0m;

        // Field 38: NAPAS merchant account information.
        var beneficiary = Tlv("00", bankBin) + Tlv("01", accountNumber);
        var merchantAccount =
            Tlv("00", NapasGuid) +
            Tlv("01", beneficiary) +
            Tlv("02", ServiceCode);

        var builder = new StringBuilder();
        builder.Append(Tlv("00", "01"));
        builder.Append(Tlv("01", isDynamic ? "12" : "11"));
        builder.Append(Tlv("38", merchantAccount));
        builder.Append(Tlv("53", CurrencyVnd));
        if (isDynamic)
            builder.Append(Tlv("54", FormatAmount(amount)));
        builder.Append(Tlv("58", CountryVn));

        var memo = FoldMemo(addInfo);
        if (!string.IsNullOrEmpty(memo))
            builder.Append(Tlv("62", Tlv("08", memo)));

        // CRC is computed over the whole payload INCLUDING the "6304" tag+length prefix.
        builder.Append("6304");
        var crc = Crc16Ccitt(builder.ToString());
        builder.Append(crc.ToString("X4", CultureInfo.InvariantCulture));

        return builder.ToString();
    }

    /// <summary>Formats a single EMVCo TLV element: 2-char id + 2-digit length + value.</summary>
    private static string Tlv(string id, string value) =>
        $"{id}{value.Length.ToString("D2", CultureInfo.InvariantCulture)}{value}";

    /// <summary>Formats the VND amount as a plain string: integer when whole, else up to two decimals; no grouping (OQ, §4.3).</summary>
    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// ASCII-folds the memo (strips diacritics, maps đ/Đ, drops non-printable-ASCII) and truncates to
    /// <see cref="MemoMaxLength"/> (OQ9). The QR still transfers correctly; the composite label keeps
    /// full Vietnamese.
    /// </summary>
    private static string FoldMemo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            var mapped = ch switch
            {
                'đ' => 'd',
                'Đ' => 'D',
                _ => ch
            };

            // Keep printable ASCII only (0x20-0x7E); replace anything else with a space.
            folded.Append(mapped is >= ' ' and <= '~' ? mapped : ' ');
        }

        var result = folded.ToString().Normalize(NormalizationForm.FormC);

        // Collapse runs of whitespace into single spaces for a clean, compact memo.
        result = string.Join(' ', result.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (result.Length > MemoMaxLength)
            result = result[..MemoMaxLength].TrimEnd();

        return result;
    }

    /// <summary>
    /// CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF, no reflect, no final xor) over the ASCII bytes of
    /// <paramref name="data"/>. Matches the published test vector "123456789" -&gt; 0x29B1.
    /// </summary>
    public static ushort Crc16Ccitt(ReadOnlySpan<char> data)
    {
        ushort crc = 0xFFFF;
        foreach (var ch in data)
        {
            crc ^= (ushort)(ch << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }

        return crc;
    }
}
