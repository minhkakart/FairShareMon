using System.Globalization;

namespace FairShareMonApi.Services.Api.Export.Formatters;

/// <summary>
/// Bộ ghi CSV nhỏ theo RFC-4180 (M8, OQ2/OQ4), không phụ thuộc thư viện ngoài. Dấu phân cách là dấu
/// phẩy, kết thúc dòng là CRLF; một ô được bọc trong dấu ngoặc kép khi và chỉ khi nó chứa dấu phẩy,
/// dấu ngoặc kép, CR hoặc LF, và dấu ngoặc kép bên trong được nhân đôi. Ngoài ra, để chống chèn công
/// thức (CSV/Excel formula injection), một ô văn bản bắt đầu bằng ký tự kích hoạt công thức được vô
/// hiệu hóa bằng cách thêm dấu nháy đơn <c>'</c> đứng trước (trước khi áp dụng quy tắc bọc RFC-4180):
/// <c>=</c>, <c>@</c>, TAB (0x09), CR (0x0D) luôn được vô hiệu hóa; <c>+</c>/<c>-</c> chỉ vô hiệu hóa
/// khi ô KHÔNG phải là số thập phân bất biến (nên tiền tệ như <c>-500000.00</c>/<c>0.00</c> giữ nguyên).
/// </summary>
public static class CsvWriter
{
    private const char Delimiter = ',';

    /// <summary>Dấu vô hiệu hóa công thức (OQ2a): dấu nháy đơn đứng trước ô nguy hiểm.</summary>
    private const char FormulaGuardPrefix = '\'';

    /// <summary>Kết thúc dòng CRLF theo RFC-4180 (OQ4).</summary>
    public const string LineEnding = "\r\n";

    /// <summary>Bọc/thoát một ô theo RFC-4180, sau khi vô hiệu hóa công thức (numeric-safe).</summary>
    public static string EscapeField(string? field)
    {
        field = NeutralizeFormula(field ?? string.Empty);

        var mustQuote = field.Contains(Delimiter)
            || field.Contains('"')
            || field.Contains('\r')
            || field.Contains('\n');

        if (!mustQuote)
            return field;

        return string.Concat("\"", field.Replace("\"", "\"\""), "\"");
    }

    /// <summary>
    /// Vô hiệu hóa chèn công thức bằng cách thêm dấu nháy đơn cho ô văn bản bắt đầu bằng ký tự kích
    /// hoạt (numeric-safe, OQ1a): <c>=</c>/<c>@</c>/TAB/CR luôn được vô hiệu hóa; <c>+</c>/<c>-</c>
    /// chỉ vô hiệu hóa khi ô KHÔNG phải số thập phân bất biến (giữ nguyên tiền tệ, kể cả số âm).
    /// </summary>
    private static string NeutralizeFormula(string field)
    {
        if (field.Length == 0)
            return field;

        var first = field[0];

        if (first is '=' or '@' or '\t' or '\r')
            return FormulaGuardPrefix + field;

        if (first is '+' or '-')
        {
            var isInvariantDecimal = decimal.TryParse(
                field,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out _);

            return isInvariantDecimal ? field : FormulaGuardPrefix + field;
        }

        return field;
    }

    /// <summary>Ghép các ô của một dòng bằng dấu phẩy, mỗi ô đã được thoát.</summary>
    public static string FormatRow(IEnumerable<string> fields) =>
        string.Join(Delimiter.ToString(), fields.Select(EscapeField));
}
