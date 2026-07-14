namespace FairShareMonApi.Models.Export;

/// <summary>
/// Kết quả xuất do <c>IExportService</c> trả về: nội dung byte đã kết xuất cùng content-type và tên tệp,
/// để controller đổ thẳng ra <c>File(...)</c> (bỏ qua bao bọc <c>ApiResult</c> - M8, OQ1).
/// </summary>
public sealed class ExportedFile(byte[] content, string contentType, string fileName)
{
    /// <summary>Nội dung tệp đã kết xuất (với CSV: UTF-8 kèm BOM).</summary>
    public byte[] Content { get; } = content;

    /// <summary>Content-Type của tệp, ví dụ <c>text/csv; charset=utf-8</c>.</summary>
    public string ContentType { get; } = contentType;

    /// <summary>Tên tệp tải về (ASCII an toàn, không chứa ký tự gây chèn header).</summary>
    public string FileName { get; } = fileName;
}
