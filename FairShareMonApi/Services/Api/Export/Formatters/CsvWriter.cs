namespace FairShareMonApi.Services.Api.Export.Formatters;

/// <summary>
/// Bộ ghi CSV nhỏ theo RFC-4180 (M8, OQ2/OQ4), không phụ thuộc thư viện ngoài. Dấu phân cách là dấu
/// phẩy, kết thúc dòng là CRLF; một ô được bọc trong dấu ngoặc kép khi và chỉ khi nó chứa dấu phẩy,
/// dấu ngoặc kép, CR hoặc LF, và dấu ngoặc kép bên trong được nhân đôi.
/// </summary>
public static class CsvWriter
{
    private const char Delimiter = ',';

    /// <summary>Kết thúc dòng CRLF theo RFC-4180 (OQ4).</summary>
    public const string LineEnding = "\r\n";

    /// <summary>Bọc/thoát một ô theo RFC-4180.</summary>
    public static string EscapeField(string? field)
    {
        field ??= string.Empty;

        var mustQuote = field.Contains(Delimiter)
            || field.Contains('"')
            || field.Contains('\r')
            || field.Contains('\n');

        if (!mustQuote)
            return field;

        return string.Concat("\"", field.Replace("\"", "\"\""), "\"");
    }

    /// <summary>Ghép các ô của một dòng bằng dấu phẩy, mỗi ô đã được thoát.</summary>
    public static string FormatRow(IEnumerable<string> fields) =>
        string.Join(Delimiter.ToString(), fields.Select(EscapeField));
}
